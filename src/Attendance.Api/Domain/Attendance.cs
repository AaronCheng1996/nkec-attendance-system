namespace Attendance.Api.Domain;

public enum AttendanceStatus : byte
{
    Present = 1,
    Absent = 2,
    Excused = 3,
}

/// <summary>聚會場次。</summary>
public class Session
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public DateOnly SessionDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public string? Title { get; set; }
    public bool IsCancelled { get; set; }

    public List<AttendanceRecord> Records { get; set; } = [];
}

/// <summary>點名紀錄。點名時整份名單都會寫入，含缺席。</summary>
public class AttendanceRecord
{
    public long Id { get; set; }
    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public AttendanceStatus Status { get; set; }

    /// <summary>點名當天不是群組成員（例如已換組的人回來參加）。</summary>
    public bool IsVisitor { get; set; }

    public string MarkedBy { get; set; } = "";
    public AppUser MarkedByUser { get; set; } = null!;
    public DateTimeOffset MarkedAt { get; set; }
}
