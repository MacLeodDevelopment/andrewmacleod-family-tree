using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyTree.Api.Tests;

public class ProfilesTests(ApiFactory factory, SqlServerContainer sqlServer) : IClassFixture<ApiFactory>
{
    private const string AdminPeople = "/api/v1/admin/people";
    private const string Families = "/api/v1/admin/families";
    private const string People = "/api/v1/people";

    private static readonly TestUser Admin = new("Andrew", "andrew@example.test", ["Family.Admin"]);
    private static readonly TestUser Viewer = new("Jean", "jean@example.test", ["Family.Viewer"]);

    private sealed record Created(Guid Id);

    private sealed record Summary(Guid Id, string Name, string? Born, string? Died);

    private sealed record Results(Summary[] Items, int Total);

    private sealed record Event(string Type, string? Date, string? Place);

    private sealed record Parent(Summary? Person, string Relationship);

    private sealed record Sibling(Summary Person, bool IsHalf);

    private sealed record Child(Summary Person, string Relationship);

    private sealed record Partnership(Summary? Partner, string Type, Event[] Events, Child[] Children);

    private sealed record Profile(Guid Id, string Sex, Event[] Events, Parent[] Parents, Sibling[] Siblings, Partnership[] Partnerships);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Api() => factory.WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:FamilyTree", sqlServer.ConnectionString));

    private static async Task<Guid> PersonAsync(HttpClient admin, string? givenName = null, string? surname = null)
    {
        var response = await admin.PostAsJsonAsync(
            AdminPeople, new { names = new[] { new { givenName = givenName ?? $"Relative {Guid.NewGuid():N}", surname } } }, Cancellation);
        return (await response.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
    }

    private static async Task<Guid> FamilyAsync(HttpClient admin, Guid? partner1, Guid? partner2, string type, params (Guid Child, string To1, string To2)[] children)
    {
        var response = await admin.PostAsJsonAsync(Families, new
        {
            partner1Id = partner1,
            partner2Id = partner2,
            type,
            children = children.Select(c => new { childId = c.Child, partner1Relationship = c.To1, partner2Relationship = c.To2 }),
        }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
    }

    private static Task<Profile?> ProfileAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<Profile>($"{People}/{id}", Cancellation);

    private static Task AddEventAsync(HttpClient admin, string address, string type, string date, string? place = null) =>
        admin.PostAsJsonAsync($"{address}/events", new { type, date, place }, Cancellation);

    [Fact]
    public async Task A_viewer_can_find_someone_with_their_dates()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var word = $"Findme{Guid.NewGuid():N}";
        var person = await PersonAsync(admin, word, "MacLeod");
        await AddEventAsync(admin, $"{AdminPeople}/{person}", "Birth", "abt 1870");
        await AddEventAsync(admin, $"{AdminPeople}/{person}", "Death", "1901");

        var found = await api.CreateClientFor(Viewer).GetFromJsonAsync<Results>($"{People}/search?q={word}", Cancellation);

        Assert.Equal(new Summary(person, $"{word} MacLeod", "c. 1870", "1901"), Assert.Single(found!.Items));
    }

    [Fact]
    public async Task Searching_matches_every_word_across_names_and_lists_results_by_name()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var word = $"Findme{Guid.NewGuid():N}";
        await PersonAsync(admin, $"Zara{word}", "Morrison");
        await PersonAsync(admin, $"Anna{word}", "Morrison");
        await PersonAsync(admin, $"Beth{word}", "Campbell");

        var found = await api.CreateClientFor(Viewer).GetFromJsonAsync<Results>($"{People}/search?q={word}%20morrison", Cancellation);

        Assert.Equal([$"Anna{word} Morrison", $"Zara{word} Morrison"], found!.Items.Select(person => person.Name));
    }

    [Fact]
    public async Task A_long_list_of_matches_is_cut_short_but_says_how_many_there_are()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var word = $"Findme{Guid.NewGuid():N}";
        for (var i = 0; i < 21; i++)
        {
            await PersonAsync(admin, $"{word}{i}");
        }

        var found = await api.CreateClientFor(Viewer).GetFromJsonAsync<Results>($"{People}/search?q={word}", Cancellation);

        Assert.Equal(20, found!.Items.Length);
        Assert.Equal(21, found.Total);
    }

    [Fact]
    public async Task Searching_for_nothing_finds_nobody()
    {
        var found = await Api().CreateClientFor(Viewer).GetFromJsonAsync<Results>($"{People}/search?q=%20", Cancellation);

        Assert.Empty(found!.Items);
    }

    [Fact]
    public async Task A_profile_shows_someones_life_and_closest_family()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (mother, father, person, sister, wife, son) = (
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin),
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await AddEventAsync(admin, $"{AdminPeople}/{person}", "Birth", "abt 1870", "Innellan");
        await FamilyAsync(admin, mother, father, "Marriage", (person, "Biological", "Adopted"), (sister, "Biological", "Biological"));
        var marriage = await FamilyAsync(admin, wife, person, "Marriage", (son, "Biological", "Step"));
        await AddEventAsync(admin, $"{Families}/{marriage}", "Marriage", "1895", "Dunoon");

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), person);

        Assert.Equal(new Event("Birth", "c. 1870", "Innellan"), Assert.Single(profile!.Events));
        Assert.Equal([(mother, "Biological"), (father, "Adopted")], profile.Parents.Select(parent => (parent.Person!.Id, parent.Relationship)));
        Assert.Equal((sister, false), Assert.Single(profile.Siblings.Select(sibling => (sibling.Person.Id, sibling.IsHalf))));
        var partnership = Assert.Single(profile.Partnerships);
        Assert.Equal(wife, partnership.Partner!.Id);
        Assert.Equal(new Event("Marriage", "1895", "Dunoon"), Assert.Single(partnership.Events));
        var child = Assert.Single(partnership.Children);
        Assert.Equal((son, "Step"), (child.Person.Id, child.Relationship));
    }

    [Fact]
    public async Task An_unknown_parent_and_an_unknown_partner_are_shown_as_unknown()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (mother, child, grandchild) = (await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, mother, null, "None", (child, "Biological", "Unknown"));
        await FamilyAsync(admin, child, null, "None", (grandchild, "Biological", "Unknown"));

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), child);

        Assert.Equal(2, profile!.Parents.Length);
        Assert.Null(profile.Parents[1].Person);
        Assert.Null(Assert.Single(profile.Partnerships).Partner);
    }

    [Fact]
    public async Task Half_siblings_are_worked_out_from_a_shared_parent()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (father, firstWife, secondWife, thirdWife, person, full, half, otherHalf, stranger) = (
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin),
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, father, firstWife, "Marriage", (person, "Biological", "Biological"), (full, "Biological", "Biological"));
        await FamilyAsync(admin, father, secondWife, "Marriage", (half, "Biological", "Biological"));
        await FamilyAsync(admin, thirdWife, father, "Marriage", (otherHalf, "Biological", "Biological"));
        await FamilyAsync(admin, secondWife, null, "None", (stranger, "Biological", "Unknown"));

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), person);

        Assert.Equivalent(
            new[] { (full, false), (half, true), (otherHalf, true) },
            profile!.Siblings.Select(sibling => (sibling.Person.Id, sibling.IsHalf)).ToArray());
    }

    [Fact]
    public async Task Someone_in_two_families_is_a_full_sibling_not_also_a_half_one()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (birthMother, person, adopted, otherParent) = (
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, birthMother, null, "None", (person, "Biological", "Unknown"), (adopted, "Biological", "Unknown"));
        await FamilyAsync(admin, birthMother, otherParent, "Marriage", (adopted, "Biological", "Step"));

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), person);

        var sibling = Assert.Single(profile!.Siblings);
        Assert.False(sibling.IsHalf);
    }

    [Fact]
    public async Task Someone_with_no_family_has_an_empty_profile_rather_than_an_error()
    {
        var api = Api();
        var person = await PersonAsync(api.CreateClientFor(Admin));

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), person);

        Assert.Empty(profile!.Parents);
        Assert.Empty(profile.Siblings);
        Assert.Empty(profile.Partnerships);
    }

    [Fact]
    public async Task A_profile_does_not_show_a_date_as_it_was_typed()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await PersonAsync(admin);
        await AddEventAsync(admin, $"{AdminPeople}/{person}", "Birth", "abt 1870");

        var body = await api.CreateClientFor(Viewer).GetStringAsync($"{People}/{person}", Cancellation);

        Assert.DoesNotContain("abt 1870", body);
    }

    [Fact]
    public async Task Asking_for_someone_who_does_not_exist_gets_not_found()
    {
        var response = await Api().CreateClientFor(Viewer).GetAsync($"{People}/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/search?q=mac")]
    [InlineData("/00000000-0000-0000-0000-000000000000")]
    public async Task Only_family_members_can_search_or_read_profiles(string path)
    {
        var api = Api();

        var visitor = await api.CreateClient().GetAsync($"{People}{path}", Cancellation);
        var noRole = await api.CreateClientFor(new TestUser("Stranger", "stranger@example.test", [])).GetAsync($"{People}{path}", Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, visitor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
    }

    [Fact]
    public async Task These_addresses_cannot_be_written_to()
    {
        var api = Api();
        var person = await PersonAsync(api.CreateClientFor(Admin));
        var client = api.CreateClientFor(Viewer);

        var update = await client.PutAsJsonAsync($"{People}/{person}", new { names = new[] { new { givenName = "Hacked" } } }, Cancellation);
        var delete = await client.DeleteAsync($"{People}/{person}", Cancellation);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, update.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
    }
}
