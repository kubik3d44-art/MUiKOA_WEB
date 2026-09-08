using MUiKOA_WEB.Models;

namespace MUiKOA_WEB.Services;

public enum AuthenticationStatus
{
    Success,
    InvalidCredentials,
    AccessDenied
}

public sealed class AuthenticationResult
{
    private AuthenticationResult(AuthenticationStatus status, AuthenticatedUser? user)
    {
        Status = status;
        User = user;
    }

    public AuthenticationStatus Status { get; }

    public AuthenticatedUser? User { get; }

    public static AuthenticationResult Success(AuthenticatedUser user) => new(AuthenticationStatus.Success, user);

    public static AuthenticationResult InvalidCredentials() => new(AuthenticationStatus.InvalidCredentials, null);

    public static AuthenticationResult AccessDenied() => new(AuthenticationStatus.AccessDenied, null);
}
