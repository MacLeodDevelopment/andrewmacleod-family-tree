using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyTree.Api.Tests;

/// <summary>
/// Signs requests in as a pretend user, standing in for Microsoft Entra ID, which
/// the tests can't reach. The user's name and roles become claims shaped like those
/// in a real Entra access token ("name" and "roles").
/// </summary>
public static class TestUsers
{
    public static HttpClient CreateClientFor<TProgram>(
        this WebApplicationFactory<TProgram> factory, string name, params string[] roles)
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

        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.NameHeader, name);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, string.Join(',', roles));
        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string NameHeader = "X-Test-User";
        public const string RolesHeader = "X-Test-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(NameHeader, out var name))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var roles = Request.Headers[RolesHeader].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var claims = new List<Claim> { new("name", name.ToString()) };
            claims.AddRange(roles.Select(role => new Claim("roles", role)));

            var identity = new ClaimsIdentity(claims, SchemeName, nameType: "name", roleType: "roles");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
