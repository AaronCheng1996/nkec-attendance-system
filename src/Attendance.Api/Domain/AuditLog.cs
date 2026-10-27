namespace Attendance.Api.Domain;

public class AuditLog
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";

    /// <summary>變更內容（JSON）。</summary>
    public string? Changes { get; set; }

    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
