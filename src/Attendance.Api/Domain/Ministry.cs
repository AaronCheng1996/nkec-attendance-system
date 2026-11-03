namespace Attendance.Api.Domain;

/// <summary>事工，例如兒童主日學、青少年、小組。</summary>
public class Ministry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Group> Groups { get; set; } = [];
}

/// <summary>主責同工負責的事工。</summary>
public class MinistryCoordinator
{
    public string UserId { get; set; } = "";
    public int MinistryId { get; set; }
}

/// <summary>群組，例如班級或小組。</summary>
public class Group
{
    public int Id { get; set; }
    public int MinistryId { get; set; }
    public Ministry Ministry { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DayOfWeek? DefaultDayOfWeek { get; set; }
    public TimeOnly? DefaultStartTime { get; set; }
    public int SortOrder { get; set; }

    /// <summary>適用年級範圍（預留：依年級分班、批次升班）。數值同 <see cref="Person.SchoolEntryYear"/> 推算出的年級。</summary>
    public int? MinGrade { get; set; }
    public int? MaxGrade { get; set; }

    public bool IsActive { get; set; } = true;

    public List<GroupMembership> Memberships { get; set; } = [];
}

/// <summary>老師 / 小組長負責的群組。</summary>
public class GroupLeader
{
    public string UserId { get; set; } = "";
    public int GroupId { get; set; }
}

/// <summary>群組成員資格。有起訖日期，升班或換組後歷史統計不受影響。</summary>
public class GroupMembership
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
