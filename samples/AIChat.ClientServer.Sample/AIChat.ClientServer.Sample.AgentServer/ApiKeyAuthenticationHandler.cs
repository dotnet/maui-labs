using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AIChat.ClientServer.Sample.AgentServer;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = configuration["AGUI_API_KEY"] ?? configuration["AGUI:ApiKey"];
        var supplied = Request.Headers.Authorization.ToString() is var authorization &&
                       authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..]
            : null;

        if (!IsValid(expected, supplied))
            return Task.FromResult(AuthenticateResult.Fail("Unauthorized"));

        var identity = new System.Security.Claims.ClaimsIdentity(SchemeName);
        identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "agui-client"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new System.Security.Claims.ClaimsPrincipal(identity), SchemeName)));
    }

    public static bool IsValid(string? expected, string? supplied) =>
        !string.IsNullOrEmpty(expected) &&
        !string.IsNullOrEmpty(supplied) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
}
