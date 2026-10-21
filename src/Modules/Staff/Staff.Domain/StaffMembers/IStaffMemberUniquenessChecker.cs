namespace Staff.Domain;

/// <summary>Port répondant à la question inter-agrégats « cet utilisateur a-t-il déjà un profil staff dans ce tenant ? ».</summary>
public interface IStaffMemberUniquenessChecker
{
    Task<bool> IsUserUniqueInTenantAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken = default);
}
