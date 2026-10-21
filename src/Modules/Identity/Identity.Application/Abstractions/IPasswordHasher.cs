using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public interface IPasswordHasher
{
    PasswordHash Hash(string password);

    bool Verify(string password, PasswordHash hash);

    bool NeedsRehash(PasswordHash hash);
}
