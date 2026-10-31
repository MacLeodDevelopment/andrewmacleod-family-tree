using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyTree.Api.Tests;

public class PeopleTests(ApiFactory factory, SqlServerContainer sqlServer) : IClassFixture<ApiFactory>
{
    private const string People = "/api/v1/admin/people";
    private const string ProblemJson = "application/problem+json";

    private static readonly TestUser Admin = new("Andrew", "andrew@example.test", ["Family.Admin"]);
    private static readonly TestUser Viewer = new("Jean", "jean@example.test", ["Family.Viewer"]);

    private sealed record Name(string GivenName, string? Surname, string Type);

    private sealed record PersonResponse(Guid Id, string Sex, Name[] Names);

    private sealed record Page(PersonResponse[] Items, int Total);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The API, connected to the real test database.</summary>
    private WebApplicationFactory<Program> Api() => factory.WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:FamilyTree", sqlServer.ConnectionString));

    [Fact]
    public async Task An_admin_can_add_a_person_and_read_them_back()
    {
        var client = Api().CreateClientFor(Admin);

        var created = await client.PostAsJsonAsync(People, new
        {
            sex = "Female",
            names = new[] { new { givenName = "Morag", surname = "MacLeod", type = "Birth" } },
        }, Cancellation);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var person = await client.GetFromJsonAsync<PersonResponse>(created.Headers.Location, Cancellation);
        Assert.NotNull(person);
        Assert.Equal("Female", person.Sex);
        Assert.Equal(new Name("Morag", "MacLeod", "Birth"), Assert.Single(person.Names));
    }

    [Fact]
    public async Task A_person_keeps_all_their_names_in_order()
    {
        var client = Api().CreateClientFor(Admin);

        var created = await client.PostAsJsonAsync(People, new
        {
            names = new[]
            {
                new { givenName = "Isla", surname = "Campbell", type = "Birth" },
                new { givenName = "Isla", surname = "MacLeod", type = "Married" },
            },
        }, Cancellation);
        var person = await client.GetFromJsonAsync<PersonResponse>(created.Headers.Location, Cancellation);

        Assert.Equal(["Campbell", "MacLeod"], person!.Names.Select(name => name.Surname));
        Assert.Equal(["Birth", "Married"], person.Names.Select(name => name.Type));
    }

    [Fact]
    public async Task Sex_can_be_left_unknown_and_a_surname_can_be_missing()
    {
        var client = Api().CreateClientFor(Admin);

        var created = await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = "Mairi" } } }, Cancellation);
        var person = await client.GetFromJsonAsync<PersonResponse>(created.Headers.Location, Cancellation);

        Assert.Equal("Unknown", person!.Sex);
        Assert.Null(Assert.Single(person.Names).Surname);
    }

    [Fact]
    public async Task Admins_can_list_people_a_page_at_a_time()
    {
        var client = Api().CreateClientFor(Admin);
        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = $"Listed {i}" } } }, Cancellation);
        }

        var page = await client.GetFromJsonAsync<Page>($"{People}?take=2", Cancellation);

        Assert.Equal(2, page!.Items.Length);
        Assert.True(page.Total >= 3, $"Expected the total to count everyone, but it was {page.Total}");
    }

    [Theory]
    [InlineData("names[0].givenName", """{"names":[{"surname":"MacLeod"}]}""")]
    [InlineData("names[0].givenName", """{"names":[{"givenName":"   "}]}""")]
    [InlineData("names", """{"names":[]}""")]
    [InlineData("names", "{}")]
    public async Task Someone_without_a_usable_name_is_refused_with_a_reason(string field, string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await Api().CreateClientFor(Admin).PostAsync(People, content, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Cancellation);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task A_name_too_long_to_store_is_refused_with_a_reason()
    {
        var response = await Api().CreateClientFor(Admin).PostAsJsonAsync(People,
            new { names = new[] { new { givenName = new string('x', 101) } } }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_sex_that_does_not_exist_is_refused()
    {
        var response = await Api().CreateClientFor(Admin).PostAsJsonAsync(People,
            new { sex = "Sometimes", names = new[] { new { givenName = "Mairi" } } }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Asking_for_a_person_who_does_not_exist_gets_not_found()
    {
        var response = await Api().CreateClientFor(Admin).GetAsync($"{People}/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Viewers_can_neither_add_nor_read_people_here()
    {
        var client = Api().CreateClientFor(Viewer);

        var add = await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = "Mairi" } } }, Cancellation);
        var list = await client.GetAsync(People, Cancellation);
        var read = await client.GetAsync($"{People}/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task Visitors_who_have_not_signed_in_are_refused()
    {
        var response = await Api().CreateClient().GetAsync(People, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
