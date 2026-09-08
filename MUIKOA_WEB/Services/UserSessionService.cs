using MUiKOA_WEB.Models;
using System.Text.Json;

namespace MUiKOA_WEB.Services;

public sealed class UserSessionService
{
    private const string SessionKey = "Session";
    private readonly IHttpContextAccessor httpContextAccessor;

    public UserSessionService(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    public AuthenticatedUser? Session { get; private set; }

    public bool IsAuthenticated => Session is not null;

    public AuthenticatedUser? InitializeFromHttpSession()
    {
        if (Session is not null)
        {
            return Session;
        }

        var serializedUser = httpContextAccessor.HttpContext?.Session.GetString(SessionKey);

        if (string.IsNullOrWhiteSpace(serializedUser))
        {
            return null;
        }

        Session = JsonSerializer.Deserialize<AuthenticatedUser>(serializedUser);
        return Session;
    }

    public void SetSession(AuthenticatedUser user)
    {
        Session = user;
        httpContextAccessor.HttpContext?.Session.SetString(SessionKey, JsonSerializer.Serialize(user));
    }

    public void Clear()
    {
        Session = null;
        httpContextAccessor.HttpContext?.Session.Remove(SessionKey);
    }
}
