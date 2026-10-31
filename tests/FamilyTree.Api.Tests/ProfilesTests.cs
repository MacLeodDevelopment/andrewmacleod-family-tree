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

    private sealed record Profile(Guid Id, bool IsPrivate, Event[] Events, Parent[] Parents, Sibling[] Siblings, Partnership[] Partnerships);

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
        await AddEventAsync(admin, $"{AdminPeople}/{wife}", "Birth", "1872");
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

    private static string Recent => (DateTime.UtcNow.Year - 40).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Someone who may be living, with details that must never reach a viewer.</summary>
    private static async Task<Guid> LivingPersonAsync(HttpClient admin, string? givenName = null)
    {
        var person = await PersonAsync(admin, givenName);
        await AddEventAsync(admin, $"{AdminPeople}/{person}", "Birth", $"3 Mar {Recent}", "Secretplace");
        return person;
    }

    [Fact]
    public async Task A_viewer_sees_a_living_persons_name_and_relatives_but_not_their_details()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (person, parent) = (await LivingPersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, parent, null, "None", (person, "Biological", "Unknown"));

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), person);
        var body = await api.CreateClientFor(Viewer).GetStringAsync($"{People}/{person}", Cancellation);

        Assert.True(profile!.IsPrivate);
        Assert.Empty(profile.Events);
        Assert.Equal(parent, profile.Parents[0].Person!.Id);
        Assert.DoesNotContain("Secretplace", body);
        Assert.DoesNotContain($"3 March {Recent}", body);
    }

    [Fact]
    public async Task Someone_with_no_dates_at_all_is_kept_private()
    {
        var api = Api();
        var person = await PersonAsync(api.CreateClientFor(Admin));

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), person);

        Assert.True(profile!.IsPrivate);
    }

    [Fact]
    public async Task A_viewer_sees_the_details_of_someone_who_has_died()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await LivingPersonAsync(admin);
        await AddEventAsync(admin, $"{AdminPeople}/{person}", "Death", "2020");

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), person);

        Assert.False(profile!.IsPrivate);
        Assert.Equal(["Birth", "Death"], profile.Events.Select(e => e.Type));
    }

    [Fact]
    public async Task An_admin_sees_a_living_persons_details()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await LivingPersonAsync(admin);

        var profile = await ProfileAsync(admin, person);

        Assert.False(profile!.IsPrivate);
        Assert.Equal("Secretplace", Assert.Single(profile.Events).Place);
    }

    [Fact]
    public async Task Searching_does_not_reveal_a_living_persons_dates()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var word = $"Findme{Guid.NewGuid():N}";
        var person = await LivingPersonAsync(admin, word);

        var asViewer = await api.CreateClientFor(Viewer).GetFromJsonAsync<Results>($"{People}/search?q={word}", Cancellation);
        var asAdmin = await admin.GetFromJsonAsync<Results>($"{People}/search?q={word}", Cancellation);

        Assert.Equal(new Summary(person, word, null, null), Assert.Single(asViewer!.Items));
        Assert.NotNull(Assert.Single(asAdmin!.Items).Born);
    }

    [Fact]
    public async Task A_living_relatives_dates_are_hidden_on_someone_elses_profile()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (ancestor, child, partner, sibling, grandchild) = (
            await PersonAsync(admin), await LivingPersonAsync(admin), await LivingPersonAsync(admin),
            await LivingPersonAsync(admin), await LivingPersonAsync(admin));
        await AddEventAsync(admin, $"{AdminPeople}/{ancestor}", "Birth", "1900");
        await AddEventAsync(admin, $"{AdminPeople}/{ancestor}", "Death", "1980");
        await FamilyAsync(admin, ancestor, partner, "Marriage", (child, "Biological", "Biological"), (grandchild, "Biological", "Biological"));
        var parentOfAncestor = await PersonAsync(admin);
        await FamilyAsync(admin, parentOfAncestor, null, "None", (ancestor, "Biological", "Unknown"), (sibling, "Biological", "Unknown"));

        var viewer = api.CreateClientFor(Viewer);
        var ancestorBody = await viewer.GetStringAsync($"{People}/{ancestor}", Cancellation);
        var profile = await ProfileAsync(viewer, ancestor);

        Assert.DoesNotContain($"3 March {Recent}", ancestorBody);
        Assert.Contains("\"born\":\"1900\"", await viewer.GetStringAsync($"{People}/{child}", Cancellation));
        Assert.Equal(partner, profile!.Partnerships[0].Partner!.Id);
        Assert.Null(profile.Partnerships[0].Partner!.Born);
    }

    [Fact]
    public async Task A_marriage_is_hidden_if_either_partner_may_be_living()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (deceased, living) = (await PersonAsync(admin), await LivingPersonAsync(admin));
        await AddEventAsync(admin, $"{AdminPeople}/{deceased}", "Birth", "1900");
        await AddEventAsync(admin, $"{AdminPeople}/{deceased}", "Death", "1980");
        var family = await FamilyAsync(admin, deceased, living, "Marriage");
        await AddEventAsync(admin, $"{Families}/{family}", "Marriage", "1975", "Secretchurch");

        var viewer = api.CreateClientFor(Viewer);
        var fromDeceased = await viewer.GetStringAsync($"{People}/{deceased}", Cancellation);
        var fromLiving = await viewer.GetStringAsync($"{People}/{living}", Cancellation);
        var asAdmin = await admin.GetStringAsync($"{People}/{deceased}", Cancellation);

        Assert.DoesNotContain("Secretchurch", fromDeceased);
        Assert.DoesNotContain("Secretchurch", fromLiving);
        Assert.Contains("Secretchurch", asAdmin);
    }

    [Fact]
    public async Task A_marriage_is_shown_when_both_partners_have_died()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (first, second) = (await PersonAsync(admin), await PersonAsync(admin));
        await AddEventAsync(admin, $"{AdminPeople}/{first}", "Death", "1980");
        await AddEventAsync(admin, $"{AdminPeople}/{second}", "Death", "1985");
        var family = await FamilyAsync(admin, first, second, "Marriage");
        await AddEventAsync(admin, $"{Families}/{family}", "Marriage", "1950", "Dunoon");

        var profile = await ProfileAsync(api.CreateClientFor(Viewer), first);

        Assert.Equal("Dunoon", Assert.Single(Assert.Single(profile!.Partnerships).Events).Place);
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
