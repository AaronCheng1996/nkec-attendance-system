using System.Globalization;
using System.Text.RegularExpressions;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Services;

public enum ImportRowStatus
{
    New,
    Duplicate,
    Error,
}

public record ImportRow(
    int Row, string Name, string? GroupName, Gender Gender, DateOnly? BirthDate, int? Grade, string? Phone,
    string? GuardianName, RelationType? GuardianRelation, ImportRowStatus Status, List<string> Messages);

public record ImportResult(bool Committed, int NewCount, int DuplicateCount, int ErrorCount, List<ImportRow> Rows);

/// <summary>The uploaded file cannot be imported at all (not Excel, missing columns, too many rows).</summary>
public class ImportFileException(string message) : Exception(message);

/// <summary>
/// Bulk import of existing members from an Excel sheet. Columns are found by header name, so their order
/// does not matter. People already in the system are skipped, never overwritten.
/// </summary>
public partial class PersonImport(AppDbContext db, GroupAccess access)
{
    public const int MaxRows = 2000;
    const string SheetName = "成員";

    static readonly (string Key, string Header, string[] Aliases)[] Columns =
    [
        ("name", "姓名*", ["姓名", "名字", "name"]),
        ("group", "群組*", ["群組", "班級", "小組", "group"]),
        ("ministry", "事工", ["事工", "ministry"]),
        ("gender", "性別", ["性別", "gender"]),
        ("birthDate", "生日", ["生日", "出生日期", "birthday", "birthdate"]),
        ("grade", "年級", ["年級", "grade"]),
        ("guardian", "家長姓名", ["家長姓名", "家長", "監護人", "parent", "guardian"]),
        ("relation", "家長關係", ["家長關係", "關係", "relation"]),
        ("phone", "電話", ["電話", "聯絡電話", "手機", "phone"]),
        ("notes", "備註", ["備註", "notes"]),
        ("externalId", "編號", ["編號", "外部編號", "學號", "id"]),
    ];

    record GroupRef(int Id, string Name, string Ministry);

    record Parsed(
        int Row, string Name, GroupRef? Group, string? GroupText, Gender Gender, DateOnly? BirthDate, int? Grade,
        string? Phone, string? GuardianName, RelationType? GuardianRelation, string? Notes, string? ExternalId,
        List<string> Errors);

    /// <summary>An empty sheet to fill in, with drop-downs for the accessible groups, gender and grade.</summary>
    public async Task<byte[]> TemplateAsync()
    {
        var groups = await AccessibleGroupsAsync();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName);
        var help = workbook.AddWorksheet("說明");

        for (var i = 0; i < Columns.Length; i++) sheet.Cell(1, i + 1).Value = Columns[i].Header;
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        var last = MaxRows + 1;
        int Col(string key) => Array.FindIndex(Columns, c => c.Key == key) + 1;
        // Text format so phone numbers and IDs keep their leading zeros.
        foreach (var key in new[] { "phone", "externalId" })
            sheet.Range(2, Col(key), last, Col(key)).Style.NumberFormat.Format = "@";
        sheet.Range(2, Col("birthDate"), last, Col("birthDate")).Style.NumberFormat.Format = "yyyy-mm-dd";

        help.Cell(1, 1).Value = "填寫方式";
        string[] notes =
        [
            "在「成員」工作表每列填一位成員，標 * 的欄位必填；欄位順序可以調整，但標題文字請保留。",
            "群組：需與系統中的群組名稱相同（見下方清單）；不同事工有同名群組時，請一併填寫「事工」。",
            "性別：男 / 女，可留空。生日：例如 2018-05-01、2018/5/1，或民國 107/5/1。",
            "年級：例如 小三、國一、高二、大班，或填數字（1–12），可留空。年級之後每年 8 月會自動升級。",
            "家長姓名、家長關係（父親 / 母親 / 監護人）：選填。有填家長姓名時，電話記在家長資料上。",
            "編號：原有名冊的編號（選填），之後再匯入時用來辨識同一個人。",
            "系統中已有同名同生日（或同編號）的成員會略過，不會覆蓋原資料。",
        ];
        for (var i = 0; i < notes.Length; i++) help.Cell(i + 2, 1).Value = notes[i];

        var groupTop = notes.Length + 3;
        help.Cell(groupTop, 1).Value = "群組";
        help.Cell(groupTop, 2).Value = "事工";
        help.Row(groupTop).Style.Font.Bold = true;
        for (var i = 0; i < groups.Count; i++)
        {
            help.Cell(groupTop + 1 + i, 1).Value = groups[i].Name;
            help.Cell(groupTop + 1 + i, 2).Value = groups[i].Ministry;
        }
        help.Column(1).Width = 90;
        help.Column(2).Width = 20;

        if (groups.Count > 0)
            sheet.Range(2, Col("group"), last, Col("group")).CreateDataValidation()
                .List(help.Range(groupTop + 1, 1, groupTop + groups.Count, 1));
        sheet.Range(2, Col("gender"), last, Col("gender")).CreateDataValidation().List("\"男,女\"");
        sheet.Range(2, Col("relation"), last, Col("relation")).CreateDataValidation().List("\"父親,母親,監護人\"");
        var gradeNames = string.Join(",", Enumerable.Range(SchoolGrade.Min, SchoolGrade.Max - SchoolGrade.Min + 1).Select(GradeName));
        sheet.Range(2, Col("grade"), last, Col("grade")).CreateDataValidation().List($"\"{gradeNames}\"");

        sheet.Columns(1, Columns.Length).Width = 14;
        sheet.Column(Col("notes")).Width = 30;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Checks every row; with <paramref name="commit"/> also creates the new members.</summary>
    public async Task<ImportResult> RunAsync(Stream file, DateOnly startDate, DateOnly today, bool commit)
    {
        var parsed = await ParseAsync(file, today);
        var groupIds = parsed.Where(p => p.Group is not null).Select(p => p.Group!.Id).Distinct().ToList();
        var names = parsed.Select(p => p.Name).Where(n => n.Length > 0).Distinct().ToList();
        var externalIds = parsed.Select(p => p.ExternalId).OfType<string>().Distinct().ToList();

        var existing = await db.People
            .Where(p => names.Contains(p.Name) || (p.ExternalId != null && externalIds.Contains(p.ExternalId)))
            .Select(p => new
            {
                p.Name,
                p.BirthDate,
                p.ExternalId,
                CurrentGroups = p.Memberships
                    .Where(m => groupIds.Contains(m.GroupId) && (m.EndDate == null || m.EndDate >= today))
                    .Select(m => m.GroupId)
                    .ToList(),
            })
            .ToListAsync();

        var rows = new List<ImportRow>();
        var toCreate = new List<Parsed>();
        var seen = new Dictionary<string, int>();
        foreach (var p in parsed)
        {
            var messages = new List<string>(p.Errors);
            var status = messages.Count > 0 ? ImportRowStatus.Error : ImportRowStatus.New;

            if (status == ImportRowStatus.New)
            {
                // Within the file: the same person listed twice.
                var key = p.ExternalId is { } id ? $"id:{id}"
                    : p.BirthDate is { } b ? $"nb:{p.Name}|{b:yyyy-MM-dd}"
                    : $"ng:{p.Name}|{p.Group!.Id}";
                if (seen.TryGetValue(key, out var firstRow))
                {
                    status = ImportRowStatus.Error;
                    messages.Add($"與第 {firstRow} 列重複");
                }
                else seen[key] = p.Row;
            }

            if (status == ImportRowStatus.New)
            {
                var match = existing.FirstOrDefault(e => p.ExternalId is not null && e.ExternalId == p.ExternalId);
                if (match is not null)
                    messages.Add($"編號 {p.ExternalId} 已存在（{match.Name}）");
                else if (p.BirthDate is not null && existing.Any(e => e.Name == p.Name && e.BirthDate == p.BirthDate))
                    messages.Add("已有同名同生日的成員");
                else if (p.BirthDate is null && existing.Any(e => e.Name == p.Name && e.CurrentGroups.Contains(p.Group!.Id)))
                    messages.Add("此群組已有同名成員");

                if (messages.Count > 0) status = ImportRowStatus.Duplicate;
                else toCreate.Add(p);
            }

            rows.Add(new ImportRow(
                p.Row, p.Name, p.Group?.Name ?? p.GroupText, p.Gender, p.BirthDate, p.Grade, p.Phone,
                p.GuardianName, p.GuardianName is null ? null : p.GuardianRelation ?? RelationType.Guardian, status, messages));
        }

        if (commit && toCreate.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var p in toCreate)
            {
                var person = new Person
                {
                    Name = p.Name,
                    Gender = p.Gender,
                    BirthDate = p.BirthDate,
                    SchoolEntryYear = p.Grade is { } grade ? SchoolGrade.EntryYear(grade, today) : null,
                    Notes = p.Notes,
                    ExternalId = p.ExternalId,
                    ConsentAt = now,
                    ConsentMethod = "Paper",
                };
                Guardians.AddContact(person, p.GuardianName, p.Phone, p.GuardianRelation);
                db.People.Add(person);
                db.GroupMemberships.Add(new GroupMembership { GroupId = p.Group!.Id, Person = person, StartDate = startDate });
            }
            await db.SaveChangesAsync();
        }

        return new ImportResult(
            commit, toCreate.Count,
            rows.Count(r => r.Status == ImportRowStatus.Duplicate),
            rows.Count(r => r.Status == ImportRowStatus.Error),
            rows);
    }

    async Task<List<GroupRef>> AccessibleGroupsAsync() =>
        await access.Groups()
            .Where(g => g.IsActive && g.Ministry.IsActive)
            .OrderBy(g => g.Ministry.SortOrder).ThenBy(g => g.SortOrder).ThenBy(g => g.Name)
            .Select(g => new GroupRef(g.Id, g.Name, g.Ministry.Name))
            .ToListAsync();

    async Task<List<Parsed>> ParseAsync(Stream file, DateOnly today)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(file);
        }
        catch (Exception)
        {
            throw new ImportFileException("無法讀取檔案，請上傳 Excel 檔（.xlsx）");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(s => s.Name == SheetName) ?? workbook.Worksheet(1);
            var header = sheet.FirstRowUsed() ?? throw new ImportFileException("工作表是空的");

            var columns = new Dictionary<string, int>();
            foreach (var cell in header.CellsUsed())
            {
                var text = cell.GetString().Replace("*", "").Replace("＊", "").Trim().ToLowerInvariant();
                var column = Columns.FirstOrDefault(c => c.Aliases.Contains(text));
                if (column.Key is not null) columns.TryAdd(column.Key, cell.Address.ColumnNumber);
            }
            if (!columns.ContainsKey("name")) throw new ImportFileException("找不到「姓名」欄，請使用匯入範本");
            if (!columns.ContainsKey("group")) throw new ImportFileException("找不到「群組」欄，請使用匯入範本");

            var dataRows = sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber()).ToList();
            if (dataRows.Count > MaxRows) throw new ImportFileException($"一次最多匯入 {MaxRows} 筆，請分批匯入");

            var groups = await AccessibleGroupsAsync();
            var result = new List<Parsed>();
            foreach (var row in dataRows)
            {
                IXLCell? Cell(string key) => columns.TryGetValue(key, out var c) ? row.Cell(c) : null;
                string? Text(string key) => Cell(key) is { } c && c.GetFormattedString().Trim() is { Length: > 0 } t ? t : null;

                // Rows that only have leftover formatting or a drop-down selection are not data.
                if (Text("name") is null && Text("group") is null && Text("birthDate") is null && Text("phone") is null) continue;

                var errors = new List<string>();
                var name = Text("name") ?? "";
                if (name.Length == 0) errors.Add("請填寫姓名");
                else if (name.Length > 50) errors.Add("姓名最多 50 個字");

                var groupText = Text("group");
                var ministryText = Text("ministry");
                GroupRef? group = null;
                if (groupText is null) errors.Add("請填寫群組");
                else
                {
                    var matches = groups
                        .Where(g => Same(g.Name, groupText) && (ministryText is null || Same(g.Ministry, ministryText)))
                        .ToList();
                    if (matches.Count == 1) group = matches[0];
                    else if (matches.Count == 0) errors.Add($"找不到群組「{groupText}」");
                    else errors.Add($"有多個名為「{groupText}」的群組，請填寫「事工」");
                }

                var gender = ParseGender(Text("gender"), errors);
                var birthDate = ParseBirthDate(Cell("birthDate"), today, errors);
                var grade = ParseGrade(Text("grade"), errors);

                var phone = Cell("phone") is { } phoneCell ? PhoneText(phoneCell) : null;
                if (phone is { Length: > 20 }) errors.Add("電話最多 20 個字");
                var guardianName = Text("guardian");
                if (guardianName is { Length: > 50 }) errors.Add("家長姓名最多 50 個字");
                var relation = ParseRelation(Text("relation"), errors);
                var notes = Text("notes");
                if (notes is { Length: > 500 }) errors.Add("備註最多 500 個字");
                var externalId = Text("externalId");
                if (externalId is { Length: > 50 }) errors.Add("編號最多 50 個字");

                result.Add(new Parsed(
                    row.RowNumber(), name, group, groupText, gender, birthDate, grade, phone, guardianName, relation,
                    notes, externalId, errors));
            }
            return result;
        }
    }

    static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    static Gender ParseGender(string? text, List<string> errors)
    {
        switch (text?.ToLowerInvariant())
        {
            case null:
                return Gender.Unknown;
            case "男" or "男生" or "m" or "male" or "boy":
                return Gender.Male;
            case "女" or "女生" or "f" or "female" or "girl":
                return Gender.Female;
            default:
                errors.Add($"性別「{text}」無法辨識，請填 男 或 女");
                return Gender.Unknown;
        }
    }

    static RelationType? ParseRelation(string? text, List<string> errors)
    {
        switch (text?.ToLowerInvariant())
        {
            case null:
                return null;
            case "父" or "父親" or "爸爸" or "爸" or "father" or "dad":
                return RelationType.Father;
            case "母" or "母親" or "媽媽" or "媽" or "mother" or "mom":
                return RelationType.Mother;
            case "監護人" or "其他" or "guardian" or "other":
                return RelationType.Guardian;
            default:
                errors.Add($"家長關係「{text}」無法辨識，請填 父親、母親 或 監護人");
                return null;
        }
    }

    static DateOnly? ParseBirthDate(IXLCell? cell, DateOnly today, List<string> errors)
    {
        if (cell is null || cell.IsEmpty()) return null;

        DateOnly? date = null;
        if (cell.DataType == XLDataType.DateTime) date = DateOnly.FromDateTime(cell.GetDateTime());
        else
        {
            var text = cell.GetFormattedString().Trim();
            var m = DateText().Match(text);
            if (m.Success)
            {
                var year = int.Parse(m.Groups["y"].Value);
                // Taiwanese (ROC) years such as 107/5/1.
                if (year < 1911) year += 1911;
                if (DateOnly.TryParseExact(
                        $"{year:0000}-{int.Parse(m.Groups["m"].Value):00}-{int.Parse(m.Groups["d"].Value):00}",
                        "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    date = parsed;
            }
        }

        if (date is null || date > today || date.Value.Year < 1900)
        {
            errors.Add($"生日「{cell.GetFormattedString().Trim()}」無法辨識，請用 2018-05-01 的格式");
            return null;
        }
        return date;
    }

    /// <summary>2018-05-01, 2018/5/1, 2018.5.1, 20180501, and ROC 107/5/1 or 1070501.</summary>
    [GeneratedRegex(@"^(?<y>\d{2,4})[-/.年](?<m>\d{1,2})[-/.月](?<d>\d{1,2})日?$|^(?<y>\d{3,4})(?<m>\d{2})(?<d>\d{2})$")]
    private static partial Regex DateText();

    static readonly string[] ChineseDigits = ["", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十", "十一", "十二"];

    static int? ParseNumber(string text)
    {
        if (int.TryParse(text, out var n)) return n;
        var i = Array.IndexOf(ChineseDigits, text);
        return i > 0 ? i : null;
    }

    static int? ParseGrade(string? text, List<string> errors)
    {
        if (text is null) return null;
        var t = text.Replace(" ", "").Replace("年級", "").Replace("年", "");

        int? grade = t switch
        {
            "幼幼班" or "幼幼" => -3,
            "小班" => -2,
            "中班" => -1,
            "大班" => 0,
            _ when t.StartsWith("國小") => ParseNumber(t[2..]),
            _ when t.StartsWith("國中") => ParseNumber(t[2..]) is { } n and <= 3 ? n + 6 : null,
            _ when t.StartsWith("高中") => ParseNumber(t[2..]) is { } n and <= 3 ? n + 9 : null,
            _ when t.StartsWith('小') => ParseNumber(t[1..]) is { } n and <= 6 ? n : null,
            _ when t.StartsWith('國') => ParseNumber(t[1..]) is { } n and <= 3 ? n + 6 : null,
            _ when t.StartsWith('高') => ParseNumber(t[1..]) is { } n and <= 3 ? n + 9 : null,
            _ => ParseNumber(t),
        };

        if (grade is null || !SchoolGrade.IsValid(grade))
        {
            errors.Add($"年級「{text}」無法辨識，例如 小三、國一、大班 或 1–12");
            return null;
        }
        return grade;
    }

    /// <summary>Phone text; restores the leading 0 Excel drops when a mobile number was typed as a number.</summary>
    static string? PhoneText(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.Number)
        {
            var digits = cell.GetDouble().ToString("0", CultureInfo.InvariantCulture);
            return digits.Length is 8 or 9 ? "0" + digits : digits;
        }
        var text = cell.GetFormattedString().Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Grade names as offered in the template's drop-down; must round-trip through <see cref="ParseGrade"/>.</summary>
    public static string GradeName(int grade) => grade switch
    {
        -3 => "幼幼班",
        -2 => "小班",
        -1 => "中班",
        0 => "大班",
        <= 6 => $"小{ChineseDigits[grade]}",
        <= 9 => $"國{ChineseDigits[grade - 6]}",
        _ => $"高{ChineseDigits[grade - 9]}",
    };
}
