namespace Attendance.Api.Domain;

public enum Gender : byte
{
    Unknown = 0,
    Male = 1,
    Female = 2,
}

public enum RelationType : byte
{
    Father = 1,
    Mother = 2,
    Guardian = 3,
}

/// <summary>人員。學生、家長、成人成員共用。</summary>
public class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
    public Gender Gender { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? FirstVisitDate { get; set; }
    public DateTimeOffset? ConsentAt { get; set; }
    public string? ConsentMethod { get; set; }
    public string? Notes { get; set; }

    /// <summary>同工自訂的延伸欄位（JSON）。</summary>
    public string? CustomFields { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<GroupMembership> Memberships { get; set; } = [];
    public List<PersonRelationship> Guardians { get; set; } = [];
}

/// <summary>孩子（Person）與家長 / 監護人（RelatedPerson）的關係。</summary>
public class PersonRelationship
{
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public int RelatedPersonId { get; set; }
    public Person RelatedPerson { get; set; } = null!;
    public RelationType RelationType { get; set; }
    public bool IsPrimaryContact { get; set; }
}
