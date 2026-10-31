using Microsoft.AspNetCore.Hosting;

namespace FamilyTree.Api.Tests;

public class CorsTests(ApiFactory factory) : IClassFixture<ApiFactory>
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
    public async Task The_family_tree_web_app_can_send_its_sign_in_token()
    {
        // Browsers ask first (a "preflight" OPTIONS request) before sending an Authorization header cross-origin.
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/me");
        preflight.Headers.Add("Origin", SpaOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await CreateClient().SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Preflight failed with {(int)response.StatusCode}");
        Assert.Contains("authorization", response.Headers.GetValues("Access-Control-Allow-Headers").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_family_tree_web_app_can_send_data_to_the_api()
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/admin/people");
        preflight.Headers.Add("Origin", SpaOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await CreateClient().SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Preflight failed with {(int)response.StatusCode}");
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Contains("content-type", response.Headers.GetValues("Access-Control-Allow-Headers").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_family_tree_web_app_can_save_changes_through_the_api()
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/admin/people/00000000-0000-0000-0000-000000000000");
        preflight.Headers.Add("Origin", SpaOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await CreateClient().SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.Contains("PUT", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    [Fact]
    public async Task The_family_tree_web_app_can_delete_through_the_api()
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/admin/people/00000000-0000-0000-0000-000000000000");
        preflight.Headers.Add("Origin", SpaOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "DELETE");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await CreateClient().SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.Contains("DELETE", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
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
