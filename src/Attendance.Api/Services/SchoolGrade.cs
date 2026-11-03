namespace Attendance.Api.Services;

/// <summary>
/// 年級以入學年度（<see cref="Domain.Person.SchoolEntryYear"/>）儲存，學年度從 8 月 1 日開始，
/// 所以每年 8 月年級自動加一，不必逐一修改資料。
/// 年級數值：1–6 國小、7–9 國中、10–12 高中；幼兒園 0 大班、-1 中班、-2 小班、-3 幼幼班。
/// </summary>
public static class SchoolGrade
{
    public const int Min = -3;
    public const int Max = 12;
    const int SchoolYearStartMonth = 8;

    /// <summary>The school year (in AD) that <paramref name="date"/> falls in, e.g. 2026-09-01 and 2027-07-31 are both 2026.</summary>
    public static int SchoolYear(DateOnly date) => date.Month >= SchoolYearStartMonth ? date.Year : date.Year - 1;

    public static int EntryYear(int grade, DateOnly today) => SchoolYear(today) - grade + 1;

    public static int? Grade(int? entryYear, DateOnly today) => entryYear is null ? null : SchoolYear(today) - entryYear + 1;

    public static bool IsValid(int? grade) => grade is null or (>= Min and <= Max);
}
