using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.Tests;

/// <summary>Every error comes back as RFC 9457 problem details, and never leaks internals.</summary>
public class ErrorResponseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ProblemJson = "application/problem+json";
    private static readonly TestUser Jean = new("Jean MacLeod", "jean@example.test", ["Family.Viewer"]);

    [Fact]
    public async Task Refusing_an_anonymous_caller_explains_itself_as_problem_details()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refusing_a_user_without_a_family_role_explains_itself_as_problem_details()
    {
        var response = await factory.CreateClientFor(new TestUser("Someone Else"))
            .GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Asking_for_something_that_does_not_exist_gets_problem_details()
    {
        var response = await factory.CreateClientFor(Jean).GetAsync("/api/v1/nothing-here", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unexpected_failure_gets_problem_details_that_reveal_nothing_internal()
    {
        var api = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, FailingRoute>()));

        var response = await api.CreateClientFor(Jean).GetAsync(FailingRoute.Path, TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(FailingRoute.Secret, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Equal((int)expected, problem?.Status);
    }

    /// <summary>Adds a route, for this test only, that fails with an unhandled exception.</summary>
    private sealed class FailingRoute : IStartupFilter
    {
        public const string Path = "/test/fails";
        public const string Secret = "connection string with a password in it";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(Path, failing => failing.Run(_ => throw new InvalidOperationException(Secret)));
        };
    }
}
