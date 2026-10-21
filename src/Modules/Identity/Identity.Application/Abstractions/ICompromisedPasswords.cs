namespace Identity.Application.Abstractions;

public interface ICompromisedPasswords
{
    Task<bool> IsCompromisedAsync(string password, CancellationToken cancellationToken = default);
}
