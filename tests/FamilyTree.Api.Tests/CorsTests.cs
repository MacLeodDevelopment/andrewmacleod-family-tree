using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyTree.Api.Tests;

public class CorsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SpaOrigin = "https://family.example.test";

    private HttpClient CreateClient() =>
        factory
            .WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins:0", SpaOrigin))
            .CreateClient();

    [Fact]
    public async Task The_family_tree_web_app_can_call_the_api_from_the_browser()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", SpaOrigin);

        var response = await CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(SpaOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Other_websites_cannot_call_the_api_from_the_browser()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://somewhere-else.example.test");

        var response = await CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
