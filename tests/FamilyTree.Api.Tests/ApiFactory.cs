using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyTree.Api.Tests;

/// <summary>
/// Hosts the API in memory with placeholder Entra ID settings. Requests without a
/// token are handled by the real sign-in pipeline; to act as a signed-in user, use
/// <see cref="TestUsers.CreateClientFor"/>.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-00000000000a");
        builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-00000000000b");
    }
}
