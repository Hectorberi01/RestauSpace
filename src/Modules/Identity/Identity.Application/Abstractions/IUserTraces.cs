namespace Identity.Application.Abstractions;

public interface IUserTraces
{
    Task ForgetAsync(UserId userId, Email previousEmail, CancellationToken cancellationToken = default);
}
