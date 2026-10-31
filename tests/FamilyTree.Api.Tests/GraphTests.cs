using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyTree.Api.Tests;

public class GraphTests(ApiFactory factory, SqlServerContainer sqlServer) : IClassFixture<ApiFactory>
{
    private const string AdminPeople = "/api/v1/admin/people";
    private const string Families = "/api/v1/admin/families";
    private const string People = "/api/v1/people";

    private static readonly TestUser Admin = new("Andrew", "andrew@example.test", ["Family.Admin"], Guid.NewGuid().ToString());
    private static readonly TestUser Viewer = new("Jean", "jean@example.test", ["Family.Viewer"], Guid.NewGuid().ToString());

    private sealed record Created(Guid Id);

    private sealed record GraphPerson(Guid Id, string Name, string? Born, string? Died, bool IsHalfSibling);

    private sealed record GraphChild(Guid ChildId, string Partner1Relationship, string Partner2Relationship);

    private sealed record GraphFamily(Guid Id, Guid? Partner1Id, Guid? Partner2Id, string Type, GraphChild[] Children);

    private sealed record Graph(Guid FocusId, GraphPerson[] People, GraphFamily[] Families)
    {
        public bool Has(Guid person) => People.Any(p => p.Id == person);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Api() => factory.WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:FamilyTree", sqlServer.ConnectionString));

    private static async Task<Guid> PersonAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync(
            AdminPeople, new { names = new[] { new { givenName = $"Relative {Guid.NewGuid():N}" } } }, Cancellation);
        return (await response.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
    }

    private static async Task<Guid> PersonAsync(HttpClient admin, string born, string? died = null)
    {
        var person = await PersonAsync(admin);
        await admin.PostAsJsonAsync($"{AdminPeople}/{person}/events", new { type = "Birth", date = born }, Cancellation);
        if (died is not null)
        {
            await admin.PostAsJsonAsync($"{AdminPeople}/{person}/events", new { type = "Death", date = died }, Cancellation);
        }

        return person;
    }

    private static async Task<Guid> FamilyAsync(HttpClient admin, Guid? partner1, Guid? partner2, params Guid[] children)
    {
        var response = await admin.PostAsJsonAsync(Families, new
        {
            partner1Id = partner1,
            partner2Id = partner2,
            type = "Marriage",
            children = children.Select(child => new { childId = child }),
        }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
    }

    private static Task<Graph?> GraphAsync(HttpClient client, Guid id, string query = "") =>
        client.GetFromJsonAsync<Graph>($"{People}/{id}/graph{query}", Cancellation);

    /// <summary>Four generations in a line: great-grandparent, grandparent, parent, person, child, grandchild.</summary>
    private async Task<(HttpClient Admin, Guid[] Line)> LineAsync(int generations = 6)
    {
        var admin = Api().CreateClientFor(Admin);
        var line = new Guid[generations];
        for (var i = 0; i < line.Length; i++)
        {
            line[i] = await PersonAsync(admin);
        }

        for (var i = 0; i < line.Length - 1; i++)
        {
            await FamilyAsync(admin, line[i], null, line[i + 1]);
        }

        return (admin, line);
    }

    [Fact]
    public async Task Asking_for_two_generations_up_stops_at_the_grandparents()
    {
        var (admin, line) = await LineAsync();

        var graph = await GraphAsync(admin, line[3], "?up=2&down=0");

        Assert.True(graph!.Has(line[3]));
        Assert.True(graph.Has(line[2]));
        Assert.True(graph.Has(line[1]));
        Assert.False(graph.Has(line[0]));
    }

    [Fact]
    public async Task Asking_for_one_generation_down_stops_at_the_children()
    {
        var (admin, line) = await LineAsync();

        var graph = await GraphAsync(admin, line[3], "?up=0&down=1");

        Assert.True(graph!.Has(line[4]));
        Assert.False(graph.Has(line[5]));
        Assert.False(graph.Has(line[2]));
    }

    [Fact]
    public async Task By_default_it_goes_three_up_and_two_down()
    {
        var (admin, line) = await LineAsync();

        var graph = await GraphAsync(admin, line[3]);

        Assert.Equal(line[3], graph!.FocusId);
        Assert.All(line, person => Assert.True(graph.Has(person)));
    }

    [Theory]
    [InlineData("?up=-5&down=-5", 1)]
    [InlineData("?up=99&down=99", 6)]
    public async Task Silly_depths_are_kept_within_limits(string query, int expectedPeople)
    {
        var (admin, line) = await LineAsync();

        var graph = await GraphAsync(admin, line[3], query);

        Assert.Equal(expectedPeople, graph!.People.Length);
    }

    [Fact]
    public async Task No_more_than_six_generations_are_followed_each_way()
    {
        var (admin, line) = await LineAsync(generations: 8);

        var graph = await GraphAsync(admin, line[7], "?up=99&down=0");

        Assert.True(graph!.Has(line[1]));
        Assert.False(graph.Has(line[0]));
    }

    [Fact]
    public async Task Each_generation_is_followed_through_both_parents()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (mothersMother, fathersFather, mother, father, person) = (
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, mothersMother, null, mother);
        await FamilyAsync(admin, null, fathersFather, father);
        await FamilyAsync(admin, mother, father, person);

        var graph = await GraphAsync(admin, person, "?up=2&down=0");

        Assert.True(graph!.Has(mothersMother));
        Assert.True(graph.Has(fathersFather));
    }

    [Fact]
    public async Task Every_partnership_comes_with_its_own_children()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (person, firstWife, secondWife, firstChild, secondChild) = (
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        var first = await FamilyAsync(admin, person, firstWife, firstChild);
        var second = await FamilyAsync(admin, person, secondWife, secondChild);

        var graph = await GraphAsync(admin, person, "?up=0&down=1");

        Assert.Equal(firstChild, Assert.Single(graph!.Families.Single(f => f.Id == first).Children).ChildId);
        Assert.Equal(secondChild, Assert.Single(graph.Families.Single(f => f.Id == second).Children).ChildId);
        Assert.True(graph.Has(firstWife));
        Assert.True(graph.Has(secondWife));
    }

    [Fact]
    public async Task An_unknown_parent_stays_empty_rather_than_being_left_out()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (mother, person) = (await PersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, mother, null, person);

        var graph = await GraphAsync(admin, person);

        var family = Assert.Single(graph!.Families);
        Assert.Equal(mother, family.Partner1Id);
        Assert.Null(family.Partner2Id);
    }

    [Fact]
    public async Task A_relationship_is_given_for_each_parent()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (mother, stepfather, person) = (await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await admin.PostAsJsonAsync(Families, new
        {
            partner1Id = mother,
            partner2Id = stepfather,
            children = new[] { new { childId = person, partner1Relationship = "Biological", partner2Relationship = "Step" } },
        }, Cancellation);

        var graph = await GraphAsync(admin, person);

        Assert.Equal(new GraphChild(person, "Biological", "Step"), Assert.Single(Assert.Single(graph!.Families).Children));
    }

    [Fact]
    public async Task Half_siblings_are_included_and_flagged()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (father, firstWife, secondWife, person, full, half) = (
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin),
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, father, firstWife, person, full);
        await FamilyAsync(admin, secondWife, father, half);

        var graph = await GraphAsync(admin, person, "?up=1&down=0");

        Assert.True(graph!.People.Single(p => p.Id == half).IsHalfSibling);
        Assert.False(graph.People.Single(p => p.Id == full).IsHalfSibling);
        Assert.False(graph.People.Single(p => p.Id == person).IsHalfSibling);
    }

    [Fact]
    public async Task A_sibling_who_is_also_in_a_parents_other_family_is_not_a_half_sibling()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (mother, stepfather, person, sister) = (
            await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, mother, null, person, sister);
        await FamilyAsync(admin, mother, stepfather, sister);

        var graph = await GraphAsync(admin, person, "?up=1&down=0");

        Assert.False(graph!.People.Single(p => p.Id == sister).IsHalfSibling);
    }

    [Fact]
    public async Task Half_siblings_need_at_least_one_generation_up()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (father, person, half) = (await PersonAsync(admin), await PersonAsync(admin), await PersonAsync(admin));
        await FamilyAsync(admin, father, null, person);
        await FamilyAsync(admin, father, await PersonAsync(admin), half);

        var graph = await GraphAsync(admin, person, "?up=0&down=0");

        Assert.Equal(person, Assert.Single(graph!.People).Id);
    }

    [Fact]
    public async Task A_viewer_sees_no_dates_for_anyone_who_may_be_living()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (ancestor, person) = (await PersonAsync(admin, "1850", "1920"), await PersonAsync(admin, "3 Mar 1985"));
        await FamilyAsync(admin, ancestor, null, person);

        var viewer = await GraphAsync(api.CreateClientFor(Viewer), person);
        var asAdmin = await GraphAsync(admin, person);

        Assert.Null(viewer!.People.Single(p => p.Id == person).Born);
        Assert.Equal("1850", viewer.People.Single(p => p.Id == ancestor).Born);
        Assert.Equal("1920", viewer.People.Single(p => p.Id == ancestor).Died);
        Assert.Equal("3 March 1985", asAdmin!.People.Single(p => p.Id == person).Born);
    }

    [Fact]
    public async Task Someone_linked_to_a_person_sees_their_own_dates_and_nobody_elses()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var (person, sister, parent) = (await PersonAsync(admin, "1985"), await PersonAsync(admin, "1987"), await PersonAsync(admin));
        await FamilyAsync(admin, parent, null, person, sister);
        var user = new TestUser("Jean", "jean@example.test", ["Family.Viewer"], Guid.NewGuid().ToString());
        var viewer = api.CreateClientFor(user);
        await viewer.PostAsync($"{People}/{person}/claim", null, Cancellation);
        await admin.PostAsync($"/api/v1/admin/account-links/{user.ObjectId}/approve", null, Cancellation);

        var graph = await GraphAsync(viewer, person);

        Assert.Equal("1985", graph!.People.Single(p => p.Id == person).Born);
        Assert.Null(graph.People.Single(p => p.Id == sister).Born);
    }

    [Fact]
    public async Task Someone_with_no_family_is_a_graph_of_one()
    {
        var api = Api();
        var person = await PersonAsync(api.CreateClientFor(Admin));

        var graph = await GraphAsync(api.CreateClientFor(Viewer), person);

        Assert.Equal(person, Assert.Single(graph!.People).Id);
        Assert.Empty(graph.Families);
    }

    [Fact]
    public async Task Asking_for_someone_who_does_not_exist_gets_not_found()
    {
        var response = await Api().CreateClientFor(Viewer).GetAsync($"{People}/{Guid.NewGuid()}/graph", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Only_family_members_can_read_it()
    {
        var api = Api();
        var address = $"{People}/{Guid.NewGuid()}/graph";

        var visitor = await api.CreateClient().GetAsync(address, Cancellation);
        var noRole = await api.CreateClientFor(new TestUser("Stranger", null, [])).GetAsync(address, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, visitor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
    }
}
