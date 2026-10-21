namespace Staff.Domain;

/// <summary>Une affectation = le rôle qu'un membre du personnel occupe dans un établissement.</summary>
public sealed class StaffAssignment : ValueObject
{
    internal StaffAssignment(LocationId locationId, RoleId roleId)
    {
        LocationId = locationId;
        RoleId = roleId;
    }

    public LocationId LocationId { get; }

    public RoleId RoleId { get; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return LocationId;
        yield return RoleId;
    }
}
