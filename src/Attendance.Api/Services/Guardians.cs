using Attendance.Api.Domain;

namespace Attendance.Api.Services;

public static class Guardians
{
    /// <summary>
    /// Records how to reach a child's family. With a guardian name the phone belongs to that guardian;
    /// without one it stays on the child, so the care list can still show it.
    /// </summary>
    public static void AddContact(Person child, string? guardianName, string? phone, RelationType? relation)
    {
        if (Clean(guardianName) is { } name)
            child.Guardians.Add(new PersonRelationship
            {
                RelatedPerson = New(name, phone, relation ?? RelationType.Guardian),
                RelationType = relation ?? RelationType.Guardian,
                IsPrimaryContact = true,
            });
        else
            child.Phone = Clean(phone);
    }

    public static Person New(string name, string? phone, RelationType relation) => new()
    {
        Name = name.Trim(),
        Phone = Clean(phone),
        Gender = relation switch
        {
            RelationType.Father => Gender.Male,
            RelationType.Mother => Gender.Female,
            _ => Gender.Unknown,
        },
    };

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
