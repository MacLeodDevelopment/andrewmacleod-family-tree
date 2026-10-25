using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace FamilyTree.Api.Tests;

public class AccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Me(string Name, string[] Roles);

    [Fact]
    public async Task Visitors_who_have_not_signed_in_are_refused()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_users_without_a_family_role_are_refused()
    {
        var client = factory.CreateClientFor("Someone Else");

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Family.Viewer")]
    [InlineData("Family.Admin")]
    public async Task Family_members_can_see_who_they_are_signed_in_as(string role)
    {
        var client = factory.CreateClientFor("Jean MacLeod", role);

        var me = await client.GetFromJsonAsync<Me>("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Equal("Jean MacLeod", me.Name);
        Assert.Equal([role], me.Roles);
    }

    [Fact]
    public void The_api_will_not_start_without_its_sign_in_settings()
    {
        using var unconfigured = new WebApplicationFactory<Program>();

        var error = Assert.Throws<OptionsValidationException>(() => unconfigured.CreateClient());

        Assert.Contains("AzureAd:ClientId is not configured", error.Message);
    }
}
