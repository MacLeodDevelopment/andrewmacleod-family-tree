using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyTree.Api.Tests;

public class RateLimitingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TestUser Jean = new("Jean MacLeod", "jean@example.test", ["Family.Viewer"]);
    private static readonly TestUser Morag = new("Morag MacLeod", "morag@example.test", ["Family.Viewer"]);

    /// <summary>A fresh API instance with small allowances, so tests can use them up quickly.</summary>
    private WebApplicationFactory<Program> ApiWithAllowance(int signedIn, int anonymous) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:SignedInPermitsPerMinute", signedIn.ToString());
            builder.UseSetting("RateLimiting:AnonymousPermitsPerMinute", anonymous.ToString());
        });

    [Fact]
    public async Task Family_members_who_send_too_many_requests_are_asked_to_wait()
    {
        var client = ApiWithAllowance(signedIn: 3, anonymous: 100).CreateClientFor(Jean);
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me", cancellationToken)).StatusCode);
        }
        var refused = await client.GetAsync("/api/v1/me", cancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Expected a Retry-After header saying how long to wait");
        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.Equal(429, problem?.Status);
    }

    [Fact]
    public async Task Each_family_member_has_their_own_allowance()
    {
        var api = ApiWithAllowance(signedIn: 2, anonymous: 100);
        var jean = api.CreateClientFor(Jean);
        var morag = api.CreateClientFor(Morag);
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            await jean.GetAsync("/api/v1/me", cancellationToken);
        }
        var moragsRequest = await morag.GetAsync("/api/v1/me", cancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await jean.GetAsync("/api/v1/me", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, moragsRequest.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_have_a_smaller_allowance_of_their_own()
    {
        var api = ApiWithAllowance(signedIn: 100, anonymous: 2);
        var anonymous = api.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        await anonymous.GetAsync("/health", cancellationToken);
        await anonymous.GetAsync("/health", cancellationToken);
        var refused = await anonymous.GetAsync("/health", cancellationToken);
        var signedIn = await api.CreateClientFor(Jean).GetAsync("/api/v1/me", cancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_behind_the_azure_proxy_are_told_apart_by_their_real_address()
    {
        // In Azure every request arrives from the ingress proxy, which passes on the
        // caller's address in X-Forwarded-For.
        var api = ApiWithAllowance(signedIn: 100, anonymous: 1);
        var cancellationToken = TestContext.Current.CancellationToken;
        HttpClient CallerAt(string address)
        {
            var client = api.CreateClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
            return client;
        }

        await CallerAt("203.0.113.1").GetAsync("/health", cancellationToken);
        var sameCallerAgain = await CallerAt("203.0.113.1").GetAsync("/health", cancellationToken);
        var differentCaller = await CallerAt("203.0.113.2").GetAsync("/health", cancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, sameCallerAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, differentCaller.StatusCode);
    }
}
