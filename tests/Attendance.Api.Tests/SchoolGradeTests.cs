using Attendance.Api.Services;

namespace Attendance.Api.Tests;

public class SchoolGradeTests
{
    [Theory]
    [InlineData("2026-07-31", 2025)]
    [InlineData("2026-08-01", 2026)]
    [InlineData("2027-01-15", 2026)]
    public void School_year_starts_in_August(string date, int expected) =>
        Assert.Equal(expected, SchoolGrade.SchoolYear(DateOnly.Parse(date)));

    [Fact]
    public void Grade_round_trips_through_entry_year()
    {
        var entryYear = SchoolGrade.EntryYear(3, Sunday);

        Assert.Equal(2024, entryYear);
        Assert.Equal(3, SchoolGrade.Grade(entryYear, Sunday));
    }

    [Fact]
    public void Grade_moves_up_when_the_new_school_year_starts()
    {
        var entryYear = SchoolGrade.EntryYear(6, new DateOnly(2026, 7, 31));

        Assert.Equal(7, SchoolGrade.Grade(entryYear, new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void Kindergarten_grades_are_zero_or_below()
    {
        var entryYear = SchoolGrade.EntryYear(0, Sunday);

        Assert.Equal(1, SchoolGrade.Grade(entryYear, Sunday.AddYears(1)));
        Assert.Null(SchoolGrade.Grade(null, Sunday));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(-3, true)]
    [InlineData(12, true)]
    [InlineData(-4, false)]
    [InlineData(13, false)]
    public void Validates_grade_range(int? grade, bool valid) => Assert.Equal(valid, SchoolGrade.IsValid(grade));
}
