using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace FamilyTree.Api.Tests;

/// <summary>
/// Hosts the API in memory with placeholder Entra ID settings. Requests without a
/// token are handled by the real sign-in pipeline; to act as a signed-in user, use
/// <see cref="TestUsers.CreateClientFor"/>. Every client from the same factory talks
/// to the same API instance (so, for example, they share its rate limiter).
/// </summary>
/// <remarks>
/// The database connection string points nowhere, so tests that don't need a database
/// stay fast. Tests that do use <see cref="SqlServerContainer"/>.
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Not "Development", which would load the developer's own user secrets and make
    /// tests behave differently on their machine than in CI.
    /// </summary>
    public const string TestEnvironment = "Testing";

    /// <summary>Nothing listens on port 1, so connecting fails straight away.</summary>
    public const string UnreachableDatabase = "Server=127.0.0.1,1;Database=familytree;User Id=nobody;Password=unused;Connect Timeout=2;Encrypt=False";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestEnvironment);
        builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-00000000000a");
        builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-00000000000b");
        builder.UseSetting("ConnectionStrings:FamilyTree", UnreachableDatabase);
        builder.ConfigureTestServices(services => services.AddTestSignIn());
    }
}
