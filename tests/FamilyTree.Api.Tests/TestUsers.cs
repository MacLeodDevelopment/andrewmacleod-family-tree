using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyTree.Api.Tests;

/// <summary>A pretend signed-in user. Null name or username means the token doesn't carry one.</summary>
public sealed record TestUser(string? Name = null, string? Username = null, string[]? Roles = null);

/// <summary>
/// Signs requests in as a pretend user, standing in for Microsoft Entra ID, which
/// the tests can't reach. The user's details become claims shaped like those in a
/// real Entra access token ("name", "preferred_username" and "roles").
/// </summary>
public static class TestUsers
{
    public static HttpClient CreateClientFor<TProgram>(this WebApplicationFactory<TProgram> factory, TestUser user)
        where TProgram : class
    {
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, null);
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                });
            }))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, JsonSerializer.Serialize(user));
        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var json))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var user = JsonSerializer.Deserialize<TestUser>(json.ToString())!;
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

            var identity = new ClaimsIdentity(claims, SchemeName, nameType: "name", roleType: "roles");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
