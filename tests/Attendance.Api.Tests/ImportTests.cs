using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Tests;

public class ImportTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    /// <summary>A workbook whose first row is <paramref name="headers"/>; cells are text unless given another type.</summary>
    static byte[] Workbook(string[] headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("成員");
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 2, c + 1).Value = rows[r][c] switch
                {
                    null => Blank.Value,
                    DateTime d => d,
                    double n => n,
                    var v => v.ToString(),
                };
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, byte[] file, bool commit, bool consent = true, string fileName = "名單.xlsx")
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", fileName);
        form.Add(new StringContent(commit ? "true" : "false"), "commit");
        form.Add(new StringContent(consent ? "true" : "false"), "consent");
        form.Add(new StringContent(Sunday.ToString("yyyy-MM-dd")), "startDate");
        return await client.PostAsync("/api/people/import", form, Ct);
    }

    static async Task<ImportResult> ResultAsync(HttpResponseMessage res)
    {
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ImportResult>(Json, Ct))!;
    }

    [Fact]
    public async Task Preview_checks_every_row_without_saving_and_commit_creates_the_new_ones()
    {
        var group = await app.CreateGroupAsync("匯入測試班");
        await app.WithDbAsync(async db =>
        {
            db.GroupMemberships.Add(new GroupMembership
            {
                GroupId = group.Id,
                Person = new Person { Name = "早就在了", BirthDate = new DateOnly(2017, 1, 1) },
                StartDate = new DateOnly(2020, 1, 1),
            });
            await db.SaveChangesAsync();
        });
        var client = await app.ClientForAsync(Roles.Admin);
        // Column order differs from the template on purpose.
        var file = Workbook(
            ["年級", "姓名", "電話", "群組", "性別", "生日"],
            ["小三", "王小明", 912345678d, "匯入測試班", "男", new DateTime(2018, 5, 1)],
            ["國一", "李小華", "0922-333-444", "匯入測試班", "女", "107/5/1"],
            [null, "早就在了", null, "匯入測試班", null, "2017-01-01"],
            [null, null, null, "匯入測試班", null, "2018-01-01"],
            [null, "陳小美", null, "不存在的班", null, null],
            ["研究所", "林小強", null, "匯入測試班", null, null],
            [null, "王小明", null, "匯入測試班", null, "2018/5/1"]);

        var preview = await ResultAsync(await UploadAsync(client, file, commit: false));

        Assert.Equal((false, 2, 1, 4), (preview.Committed, preview.NewCount, preview.DuplicateCount, preview.ErrorCount));
        var byRow = preview.Rows.ToDictionary(r => r.Row);
        Assert.Equal((ImportRowStatus.New, 3, Gender.Male, "0912345678"), (byRow[2].Status, byRow[2].Grade, byRow[2].Gender, byRow[2].Phone));
        Assert.Equal((ImportRowStatus.New, 7, new DateOnly(2018, 5, 1)), (byRow[3].Status, byRow[3].Grade, byRow[3].BirthDate));
        Assert.Equal(ImportRowStatus.Duplicate, byRow[4].Status);
        Assert.Contains("請填寫姓名", byRow[5].Messages);
        Assert.Contains(byRow[6].Messages, m => m.Contains("找不到群組"));
        Assert.Contains(byRow[7].Messages, m => m.Contains("年級"));
        Assert.Contains("與第 2 列重複", byRow[8].Messages);
        Assert.False(await app.WithDbAsync(db => db.People.AnyAsync(p => p.Name == "王小明")));

        var committed = await ResultAsync(await UploadAsync(client, file, commit: true));

        Assert.Equal((true, 2), (committed.Committed, committed.NewCount));
        var created = await app.WithDbAsync(db => db.People
            .Where(p => p.Name == "王小明" || p.Name == "李小華")
            .Select(p => new { p.Name, p.SchoolEntryYear, p.ConsentMethod, Start = p.Memberships.Single().StartDate })
            .ToListAsync());
        Assert.Equal(2, created.Count);
        Assert.All(created, p => Assert.Equal(("Paper", Sunday), (p.ConsentMethod, p.Start)));

        // Importing the same file again creates nobody.
        var again = await ResultAsync(await UploadAsync(client, file, commit: true));
        Assert.Equal((0, 3), (again.NewCount, again.DuplicateCount));
    }

    [Fact]
    public async Task External_id_identifies_people_across_imports()
    {
        await app.CreateGroupAsync("編號班");
        var client = await app.ClientForAsync(Roles.Admin);
        string[] headers = ["姓名", "群組", "編號"];

        await ResultAsync(await UploadAsync(client, Workbook(headers, ["張三", "編號班", "A001"]), commit: true));
        var renamed = await ResultAsync(await UploadAsync(client, Workbook(headers, ["張三豐", "編號班", "A001"]), commit: false));

        var row = Assert.Single(renamed.Rows);
        Assert.Equal(ImportRowStatus.Duplicate, row.Status);
        Assert.Contains(row.Messages, m => m.Contains("A001"));
    }

    [Fact]
    public async Task Same_group_name_in_two_ministries_needs_the_ministry_column()
    {
        await app.CreateGroupAsync("同名班");
        var youthId = await app.WithDbAsync(async db =>
        {
            var youth = new Ministry { Name = "青少年事工" };
            db.Ministries.Add(youth);
            await db.SaveChangesAsync();
            return youth.Id;
        });
        var youthGroup = await app.CreateGroupAsync("同名班", youthId);
        var client = await app.ClientForAsync(Roles.Admin);

        var withoutMinistry = await ResultAsync(await UploadAsync(client, Workbook(["姓名", "群組"], ["甲", "同名班"]), commit: false));
        var withMinistry = await ResultAsync(await UploadAsync(client, Workbook(["姓名", "群組", "事工"], ["甲", "同名班", "青少年事工"]), commit: true));

        Assert.Contains(Assert.Single(withoutMinistry.Rows).Messages, m => m.Contains("請填寫「事工」"));
        Assert.Equal(ImportRowStatus.New, Assert.Single(withMinistry.Rows).Status);
        var groupId = await app.WithDbAsync(db =>
            db.GroupMemberships.Where(m => m.Person.Name == "甲").Select(m => m.GroupId).SingleAsync());
        Assert.Equal(youthGroup.Id, groupId);
    }

    [Fact]
    public async Task Every_grade_in_the_template_drop_down_is_understood()
    {
        await app.CreateGroupAsync("年級班");
        var client = await app.ClientForAsync(Roles.Admin);
        var grades = Enumerable.Range(SchoolGrade.Min, SchoolGrade.Max - SchoolGrade.Min + 1).ToList();
        var rows = grades.Select(g => new object?[] { $"年級{g}", "年級班", PersonImport.GradeName(g) }).ToArray();

        var result = await ResultAsync(await UploadAsync(client, Workbook(["姓名", "群組", "年級"], rows), commit: false));

        Assert.Equal(grades, result.Rows.Select(r => r.Grade!.Value));
    }

    [Fact]
    public async Task Template_lists_the_groups_and_can_be_read_back()
    {
        await app.CreateGroupAsync("範本班");
        var client = await app.ClientForAsync(Roles.Admin);

        var res = await client.GetAsync("/api/people/import/template", Ct);

        res.EnsureSuccessStatusCode();
        var bytes = await res.Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            Assert.Equal("姓名*", workbook.Worksheet("成員").Cell(1, 1).GetString());
            Assert.Contains(workbook.Worksheet("說明").CellsUsed(), c => c.GetString() == "範本班");
        }
        var empty = await ResultAsync(await UploadAsync(client, bytes, commit: false));
        Assert.Empty(empty.Rows);
    }

    [Fact]
    public async Task Unusable_files_are_rejected()
    {
        var client = await app.ClientForAsync(Roles.Admin);

        var notExcel = await UploadAsync(client, "姓名,群組\n甲,乙"u8.ToArray(), commit: false, fileName: "名單.csv");
        var noGroupColumn = await UploadAsync(client, Workbook(["姓名"], ["甲"]), commit: false);
        var noConsent = await UploadAsync(client, Workbook(["姓名", "群組"], ["甲", "乙"]), commit: true, consent: false);

        Assert.Equal(HttpStatusCode.BadRequest, notExcel.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noGroupColumn.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
    }

    [Fact]
    public async Task Leaders_cannot_import()
    {
        var leader = await app.ClientForAsync(Roles.Leader);

        var res = await UploadAsync(leader, Workbook(["姓名", "群組"], ["甲", "乙"]), commit: false);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
