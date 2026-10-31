using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyTree.Api.Tests;

public class AccountLinksTests(ApiFactory factory, SqlServerContainer sqlServer) : IClassFixture<ApiFactory>
{
    private const string AdminPeople = "/api/v1/admin/people";
    private const string Families = "/api/v1/admin/families";
    private const string People = "/api/v1/people";
    private const string Links = "/api/v1/admin/account-links";
    private const string MyLinkAddress = "/api/v1/me/link";

    private static readonly TestUser Admin = new("Andrew", "andrew@example.test", ["Family.Admin"], Guid.NewGuid().ToString());

    private sealed record Created(Guid Id);

    private sealed record MyLink(Guid PersonId, bool IsApproved);

    private sealed record Link(Guid AccountId, string? DisplayName, string? Username, bool IsApproved, Guid PersonId, string PersonName);

    private sealed record Event(string Type, string? Date, string? Place);

    private sealed record Partnership(Event[] Events);

    private sealed record Profile(bool IsPrivate, Event[] Events, Partnership[] Partnerships);

    private sealed record Summary(Guid Id, string? Born);

    private sealed record Results(Summary[] Items);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Api() => factory.WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:FamilyTree", sqlServer.ConnectionString));

    /// <summary>A viewer with an account of their own, as the tests share a database.</summary>
    private static TestUser NewViewer() => new("Jean", "jean@example.test", ["Family.Viewer"], Guid.NewGuid().ToString());

    private static Guid AccountOf(TestUser user) => Guid.Parse(user.ObjectId!);

    /// <summary>Someone who may be living, with a date that a viewer shouldn't see.</summary>
    private static async Task<Guid> LivingPersonAsync(HttpClient admin, string? givenName = null)
    {
        var name = givenName ?? $"Relative {Guid.NewGuid():N}";
        var response = await admin.PostAsJsonAsync(AdminPeople, new { names = new[] { new { givenName = name } } }, Cancellation);
        var id = (await response.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
        await admin.PostAsJsonAsync($"{AdminPeople}/{id}/events", new { type = "Birth", date = "1985", place = "Dunoon" }, Cancellation);
        return id;
    }

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid person) =>
        client.PostAsync($"{People}/{person}/claim", null, Cancellation);

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient admin, TestUser user) =>
        admin.PostAsync($"{Links}/{AccountOf(user)}/approve", null, Cancellation);

    private static Task<Profile?> ProfileAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<Profile>($"{People}/{id}", Cancellation);

    [Fact]
    public async Task Someone_with_no_link_has_nothing_to_show()
    {
        var response = await Api().CreateClientFor(NewViewer()).GetAsync(MyLinkAddress, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Asking_to_be_someone_reveals_nothing_until_an_admin_approves()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await LivingPersonAsync(admin);
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);

        var claimed = await ClaimAsync(viewer, person);

        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
        Assert.Equal(new MyLink(person, false), await viewer.GetFromJsonAsync<MyLink>(MyLinkAddress, Cancellation));
        var profile = await ProfileAsync(viewer, person);
        Assert.True(profile!.IsPrivate);
        Assert.Empty(profile.Events);
    }

    [Fact]
    public async Task An_admin_sees_who_is_asking_and_for_whom()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var name = $"Relative {Guid.NewGuid():N}";
        var person = await LivingPersonAsync(admin, name);
        var user = NewViewer();
        await ClaimAsync(api.CreateClientFor(user), person);

        var links = await admin.GetFromJsonAsync<Link[]>(Links, Cancellation);

        Assert.Contains(new Link(AccountOf(user), "Jean", "jean@example.test", false, person, name), links!);
    }

    [Fact]
    public async Task Once_approved_someone_sees_their_own_record_in_full()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await LivingPersonAsync(admin);
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);

        var approved = await ApproveAsync(admin, user);

        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        Assert.Equal(new MyLink(person, true), await viewer.GetFromJsonAsync<MyLink>(MyLinkAddress, Cancellation));
        var profile = await ProfileAsync(viewer, person);
        Assert.False(profile!.IsPrivate);
        Assert.Equal(new Event("Birth", "1985", "Dunoon"), Assert.Single(profile.Events));
    }

    [Fact]
    public async Task Being_approved_as_one_person_reveals_nobody_else()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (person, relative) = (await LivingPersonAsync(admin), await LivingPersonAsync(admin));
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);
        await ApproveAsync(admin, user);

        var profile = await ProfileAsync(viewer, relative);

        Assert.True(profile!.IsPrivate);
        Assert.Empty(profile.Events);
    }

    [Fact]
    public async Task A_linked_person_finds_their_own_dates_in_search()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var word = $"Findme{Guid.NewGuid():N}";
        var person = await LivingPersonAsync(admin, word);
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);
        await ApproveAsync(admin, user);

        var found = await viewer.GetFromJsonAsync<Results>($"{People}/search?q={word}", Cancellation);

        Assert.Equal("1985", Assert.Single(found!.Items).Born);
    }

    [Fact]
    public async Task A_linked_person_sees_their_own_marriage_even_though_their_partner_is_living()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (person, partner) = (await LivingPersonAsync(admin), await LivingPersonAsync(admin));
        var family = await admin.PostAsJsonAsync(Families, new { partner1Id = person, partner2Id = partner, type = "Marriage" }, Cancellation);
        var familyId = (await family.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
        await admin.PostAsJsonAsync($"{Families}/{familyId}/events", new { type = "Marriage", date = "2010", place = "Oban" }, Cancellation);
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);
        await ApproveAsync(admin, user);
        var stranger = api.CreateClientFor(NewViewer());

        var own = await ProfileAsync(viewer, person);
        var partnersPage = await ProfileAsync(viewer, partner);
        var strangersView = await ProfileAsync(stranger, person);

        Assert.Equal("Oban", Assert.Single(Assert.Single(own!.Partnerships).Events).Place);
        Assert.Equal("Oban", Assert.Single(Assert.Single(partnersPage!.Partnerships).Events).Place);
        Assert.Empty(Assert.Single(strangersView!.Partnerships).Events);
    }

    [Fact]
    public async Task Being_approved_reveals_nobody_elses_marriage()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (person, parent, parentsPartner) = (await LivingPersonAsync(admin), await LivingPersonAsync(admin), await LivingPersonAsync(admin));
        var family = await admin.PostAsJsonAsync(Families, new
        {
            partner1Id = parent,
            partner2Id = parentsPartner,
            type = "Marriage",
            children = new[] { new { childId = person } },
        }, Cancellation);
        var familyId = (await family.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
        await admin.PostAsJsonAsync($"{Families}/{familyId}/events", new { type = "Marriage", date = "1980", place = "Oban" }, Cancellation);
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);
        await ApproveAsync(admin, user);

        var parentsPage = await ProfileAsync(viewer, parent);

        Assert.Empty(Assert.Single(parentsPage!.Partnerships).Events);
    }

    [Fact]
    public async Task Asking_again_before_approval_changes_who_it_is_for()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (wrong, right) = (await LivingPersonAsync(admin), await LivingPersonAsync(admin));
        var viewer = api.CreateClientFor(NewViewer());
        await ClaimAsync(viewer, wrong);

        await ClaimAsync(viewer, right);

        Assert.Equal(new MyLink(right, false), await viewer.GetFromJsonAsync<MyLink>(MyLinkAddress, Cancellation));
    }

    [Fact]
    public async Task An_approved_account_cannot_move_itself_to_someone_else()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (person, other) = (await LivingPersonAsync(admin), await LivingPersonAsync(admin));
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);
        await ApproveAsync(admin, user);

        var response = await ClaimAsync(viewer, other);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new MyLink(person, true), await viewer.GetFromJsonAsync<MyLink>(MyLinkAddress, Cancellation));
        Assert.True((await ProfileAsync(viewer, other))!.IsPrivate);
    }

    [Fact]
    public async Task Removing_a_link_takes_the_access_away()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await LivingPersonAsync(admin);
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);
        await ApproveAsync(admin, user);

        var removed = await admin.DeleteAsync($"{Links}/{AccountOf(user)}", Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await viewer.GetAsync(MyLinkAddress, Cancellation)).StatusCode);
        Assert.True((await ProfileAsync(viewer, person))!.IsPrivate);
    }

    [Fact]
    public async Task Deleting_a_person_removes_any_link_to_them()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await LivingPersonAsync(admin);
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);

        var deleted = await admin.DeleteAsync($"{AdminPeople}/{person}", Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await viewer.GetAsync(MyLinkAddress, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task A_viewer_cannot_approve_or_see_requests()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await LivingPersonAsync(admin);
        var user = NewViewer();
        var viewer = api.CreateClientFor(user);
        await ClaimAsync(viewer, person);

        var approve = await ApproveAsync(viewer, user);
        var list = await viewer.GetAsync(Links, Cancellation);
        var remove = await viewer.DeleteAsync($"{Links}/{AccountOf(user)}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        Assert.True((await ProfileAsync(viewer, person))!.IsPrivate);
    }

    [Fact]
    public async Task Links_that_do_not_exist_get_not_found()
    {
        var client = Api().CreateClientFor(Admin);

        var approve = await client.PostAsync($"{Links}/{Guid.NewGuid()}/approve", null, Cancellation);
        var remove = await client.DeleteAsync($"{Links}/{Guid.NewGuid()}", Cancellation);
        var claim = await ClaimAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, claim.StatusCode);
    }

    [Fact]
    public async Task An_account_without_an_id_cannot_ask()
    {
        var api = Api();
        var person = await LivingPersonAsync(api.CreateClientFor(Admin));

        var response = await ClaimAsync(api.CreateClientFor(new TestUser("Jean", "jean@example.test", ["Family.Viewer"])), person);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Only_family_members_can_ask()
    {
        var api = Api();
        var person = await LivingPersonAsync(api.CreateClientFor(Admin));

        var visitor = await ClaimAsync(api.CreateClient(), person);
        var noRole = await ClaimAsync(api.CreateClientFor(new TestUser("Stranger", null, [], Guid.NewGuid().ToString())), person);

        Assert.Equal(HttpStatusCode.Unauthorized, visitor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
    }
}
