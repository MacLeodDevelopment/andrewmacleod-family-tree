using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyTree.Api.Tests;

/// <summary>A pretend signed-in user. Null name or username means the token doesn't carry one.</summary>
public sealed record TestUser(string? Name = null, string? Username = null, string[]? Roles = null);

/// <summary>
/// Signs requests in as a pretend user, standing in for Microsoft Entra ID, which the
/// tests can't reach. The user's details become claims shaped like those in a real
/// Entra access token ("name", "preferred_username" and "roles").
/// </summary>
public static class TestUsers
{
    private const string TestScheme = "Test";
    private const string TestOrEntraScheme = "TestOrEntra";
    private const string UserHeader = "X-Test-User";

    /// <summary>Microsoft.Identity.Web's scheme, which validates real Entra tokens.</summary>
    private const string EntraScheme = "Bearer";

    /// <summary>
    /// Requests carrying a test user are signed in as that user; all others go through
    /// the real Microsoft.Identity.Web pipeline, exactly as in production.
    /// </summary>
    public static void AddTestSignIn(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestScheme, null)
            .AddPolicyScheme(TestOrEntraScheme, null, options =>
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey(UserHeader) ? TestScheme : EntraScheme);

        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = TestOrEntraScheme;
            options.DefaultAuthenticateScheme = TestOrEntraScheme;
            options.DefaultChallengeScheme = TestOrEntraScheme;
        });
    }

    /// <summary>A client whose requests are made as <paramref name="user"/>, against the same API instance.</summary>
    public static HttpClient CreateClientFor<TProgram>(this WebApplicationFactory<TProgram> factory, TestUser user)
        where TProgram : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserHeader, JsonSerializer.Serialize(user));
        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = JsonSerializer.Deserialize<TestUser>(Request.Headers[UserHeader].ToString())!;
            var claims = new List<Claim>();
            if (user.Name is not null)
            {
                claims.Add(new Claim("name", user.Name));
            }
            if (user.Username is not null)
            {
                claims.Add(new Claim("preferred_username", user.Username));
            }
            claims.AddRange((user.Roles ?? []).Select(role => new Claim("roles", role)));

            var identity = new ClaimsIdentity(claims, TestScheme, nameType: "name", roleType: "roles");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
