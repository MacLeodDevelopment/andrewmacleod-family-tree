using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyTree.Api.Tests;

public class FamiliesTests(ApiFactory factory, SqlServerContainer sqlServer) : IClassFixture<ApiFactory>
{
    private const string People = "/api/v1/admin/people";
    private const string Families = "/api/v1/admin/families";

    private static readonly TestUser Admin = new("Andrew", "andrew@example.test", ["Family.Admin"]);
    private static readonly TestUser Viewer = new("Jean", "jean@example.test", ["Family.Viewer"]);

    private sealed record Created(Guid Id);

    private sealed record Child(Guid ChildId, string Partner1Relationship, string Partner2Relationship);

    private sealed record FamilyEvent(Guid Id, string Type, string? Date, string? DateAsEntered, string? Place);

    private sealed record FamilyResponse(Guid Id, Guid? Partner1Id, Guid? Partner2Id, string Type, Child[] Children, FamilyEvent[] Events);

    private sealed record PersonFamilies(FamilyResponse[] AsChild, FamilyResponse[] AsPartner, Created[] People);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private HttpClient AdminClient() => Api().CreateClientFor(Admin);

    private WebApplicationFactory<Program> Api() => factory.WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:FamilyTree", sqlServer.ConnectionString));

    private static async Task<Guid> PersonAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            People, new { names = new[] { new { givenName = $"Relative {Guid.NewGuid():N}" } } }, Cancellation);
        return (await response.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
    }

    private static async Task<Guid> FamilyAsync(HttpClient client, Guid? partner1, Guid? partner2 = null, string type = "Marriage")
    {
        var response = await client.PostAsJsonAsync(Families, new { partner1Id = partner1, partner2Id = partner2, type }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
    }

    private static Task<HttpResponseMessage> AddChildAsync(HttpClient client, Guid family, Guid child, string toPartner1 = "Biological", string toPartner2 = "Biological") =>
        client.PostAsJsonAsync($"{Families}/{family}/children",
            new { childId = child, partner1Relationship = toPartner1, partner2Relationship = toPartner2 }, Cancellation);

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Cancellation);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task A_couple_and_their_children_can_be_recorded_and_read_back()
    {
        var client = AdminClient();
        var (mother, father, first, second) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));

        var family = await FamilyAsync(client, mother, father);
        await AddChildAsync(client, family, first);
        await AddChildAsync(client, family, second);

        var saved = await client.GetFromJsonAsync<FamilyResponse>($"{Families}/{family}", Cancellation);
        Assert.Equal(mother, saved!.Partner1Id);
        Assert.Equal(father, saved.Partner2Id);
        Assert.Equal("Marriage", saved.Type);
        Assert.Equal([first, second], saved.Children.Select(child => child.ChildId));
    }

    [Fact]
    public async Task A_persons_families_come_with_everyone_in_them()
    {
        var client = AdminClient();
        var (mother, father, child, sibling, stranger) = (
            await PersonAsync(client), await PersonAsync(client), await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, mother, father);
        await AddChildAsync(client, family, child);
        await AddChildAsync(client, family, sibling);

        var families = await client.GetFromJsonAsync<PersonFamilies>($"{People}/{child}/families", Cancellation);

        Assert.Equivalent(new[] { mother, father, child, sibling }, families!.People.Select(person => person.Id).ToArray());
        Assert.DoesNotContain(stranger, families.People.Select(person => person.Id));
    }

    [Fact]
    public async Task A_family_can_be_recorded_together_with_its_first_child()
    {
        var client = AdminClient();
        var (mother, child) = (await PersonAsync(client), await PersonAsync(client));

        var response = await client.PostAsJsonAsync(Families, new
        {
            partner1Id = mother,
            type = "None",
            children = new[] { new { childId = child, partner1Relationship = "Adopted" } },
        }, Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var family = Assert.Single((await client.GetFromJsonAsync<PersonFamilies>($"{People}/{child}/families", Cancellation))!.AsChild);
        Assert.Equal(new Child(child, "Adopted", "Unknown"), Assert.Single(family.Children));
    }

    [Fact]
    public async Task A_child_who_is_refused_leaves_no_family_behind()
    {
        var client = AdminClient();
        var (parent, child) = (await PersonAsync(client), await PersonAsync(client));
        await AddChildAsync(client, await FamilyAsync(client, parent), child);

        var loop = await client.PostAsJsonAsync(Families,
            new { partner1Id = child, children = new[] { new { childId = parent } } }, Cancellation);
        var missing = await client.PostAsJsonAsync(Families,
            new { partner1Id = child, children = new[] { new { childId = Guid.NewGuid() } } }, Cancellation);

        await AssertRefusedAsync(loop, "childId");
        await AssertRefusedAsync(missing, "childId");
        var families = await client.GetFromJsonAsync<PersonFamilies>($"{People}/{child}/families", Cancellation);
        Assert.Empty(families!.AsPartner);
    }

    [Fact]
    public async Task A_child_of_a_mother_with_no_known_partner_is_accepted()
    {
        var client = AdminClient();
        var (mother, child) = (await PersonAsync(client), await PersonAsync(client));

        var family = await FamilyAsync(client, mother, type: "None");
        var added = await AddChildAsync(client, family, child);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var saved = await client.GetFromJsonAsync<FamilyResponse>($"{Families}/{family}", Cancellation);
        Assert.Null(saved!.Partner2Id);
        Assert.Equal(new Child(child, "Biological", "Unknown"), Assert.Single(saved.Children));
    }

    [Fact]
    public async Task After_a_remarriage_each_child_stays_with_the_right_partnership()
    {
        var client = AdminClient();
        var (mother, firstHusband, secondHusband) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        var (elder, younger) = (await PersonAsync(client), await PersonAsync(client));

        var firstMarriage = await FamilyAsync(client, mother, firstHusband);
        var secondMarriage = await FamilyAsync(client, mother, secondHusband);
        await AddChildAsync(client, firstMarriage, elder);
        await AddChildAsync(client, secondMarriage, younger);

        var families = await client.GetFromJsonAsync<PersonFamilies>($"{People}/{mother}/families", Cancellation);
        Assert.Equal(2, families!.AsPartner.Length);
        var parentsOfYounger = await client.GetFromJsonAsync<PersonFamilies>($"{People}/{younger}/families", Cancellation);
        Assert.Equal(secondHusband, Assert.Single(parentsOfYounger!.AsChild).Partner2Id);
    }

    [Fact]
    public async Task A_person_can_be_a_child_in_a_birth_family_and_an_adoptive_family()
    {
        var client = AdminClient();
        var (birthMother, adoptiveMother, adoptiveFather, child) =
            (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));

        await AddChildAsync(client, await FamilyAsync(client, birthMother, type: "None"), child);
        await AddChildAsync(client, await FamilyAsync(client, adoptiveMother, adoptiveFather), child, "Adopted", "Adopted");

        var families = await client.GetFromJsonAsync<PersonFamilies>($"{People}/{child}/families", Cancellation);
        Assert.Equal(2, families!.AsChild.Length);
        Assert.Contains(families.AsChild, family => family.Children.Single().Partner1Relationship == "Adopted");
    }

    [Fact]
    public async Task A_child_can_be_biological_to_one_partner_and_a_step_child_to_the_other()
    {
        var client = AdminClient();
        var (mother, stepfather, child) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, mother, stepfather);

        await AddChildAsync(client, family, child, "Biological", "Step");

        var saved = await client.GetFromJsonAsync<FamilyResponse>($"{Families}/{family}", Cancellation);
        Assert.Equal(new Child(child, "Biological", "Step"), Assert.Single(saved!.Children));
    }

    [Fact]
    public async Task Nobody_can_become_their_own_ancestor()
    {
        var client = AdminClient();
        var (grandparent, parent, child) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        await AddChildAsync(client, await FamilyAsync(client, grandparent), parent);
        await AddChildAsync(client, await FamilyAsync(client, parent), child);

        var response = await AddChildAsync(client, await FamilyAsync(client, child), grandparent);

        await AssertRefusedAsync(response, "childId");
    }

    [Fact]
    public async Task The_ancestor_check_follows_adoptive_links_and_either_partner()
    {
        var client = AdminClient();
        var (parent, partner, child) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        await AddChildAsync(client, await FamilyAsync(client, parent), child, "Adopted");

        var response = await AddChildAsync(client, await FamilyAsync(client, partner, child), parent);

        await AssertRefusedAsync(response, "childId");
    }

    [Fact]
    public async Task The_ancestor_check_counts_both_partners_as_parents()
    {
        var client = AdminClient();
        var (firstPartner, secondPartner, child) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        await AddChildAsync(client, await FamilyAsync(client, firstPartner, secondPartner), child);

        var response = await AddChildAsync(client, await FamilyAsync(client, child), secondPartner);

        await AssertRefusedAsync(response, "childId");
    }

    [Fact]
    public async Task Nobody_can_be_their_own_parent_or_partner()
    {
        var client = AdminClient();
        var person = await PersonAsync(client);

        var ownChild = await AddChildAsync(client, await FamilyAsync(client, person), person);
        var ownPartner = await client.PostAsJsonAsync(Families, new { partner1Id = person, partner2Id = person }, Cancellation);

        await AssertRefusedAsync(ownChild, "childId");
        await AssertRefusedAsync(ownPartner, "partner2Id");
    }

    [Fact]
    public async Task A_family_needs_at_least_one_known_partner()
    {
        var response = await AdminClient().PostAsJsonAsync(Families, new { type = "Marriage" }, Cancellation);

        await AssertRefusedAsync(response, "partner1Id");
    }

    [Fact]
    public async Task People_who_do_not_exist_are_refused()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));

        var noPartner = await client.PostAsJsonAsync(Families, new { partner1Id = Guid.NewGuid() }, Cancellation);
        var noChild = await AddChildAsync(client, family, Guid.NewGuid());

        await AssertRefusedAsync(noPartner, "partner1Id");
        await AssertRefusedAsync(noChild, "childId");
    }

    [Fact]
    public async Task A_child_cannot_be_added_to_the_same_family_twice()
    {
        var client = AdminClient();
        var (parent, child) = (await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, parent);
        await AddChildAsync(client, family, child);

        var response = await AddChildAsync(client, family, child);

        await AssertRefusedAsync(response, "childId");
    }

    [Fact]
    public async Task Someone_in_a_family_cannot_be_deleted_until_their_links_are_removed()
    {
        var client = AdminClient();
        var (parent, child) = (await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, parent);
        await AddChildAsync(client, family, child);

        var whileLinked = await client.DeleteAsync($"{People}/{child}", Cancellation);
        var unlinked = await client.DeleteAsync($"{Families}/{family}/children/{child}", Cancellation);
        var afterUnlinking = await client.DeleteAsync($"{People}/{child}", Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, whileLinked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, afterUnlinking.StatusCode);
    }

    private static Task<HttpResponseMessage> NamePartnerAsync(HttpClient client, Guid family, Guid partner, string relationship = "Biological") =>
        client.PostAsJsonAsync($"{Families}/{family}/partners", new { partnerId = partner, relationship }, Cancellation);

    private static Task<FamilyResponse?> ReadAsync(HttpClient client, Guid family) =>
        client.GetFromJsonAsync<FamilyResponse>($"{Families}/{family}", Cancellation);

    [Fact]
    public async Task An_unknown_father_can_be_named_later_and_becomes_a_parent_of_the_children()
    {
        var client = AdminClient();
        var (mother, father, child) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, mother, type: "None");
        await AddChildAsync(client, family, child);

        var named = await NamePartnerAsync(client, family, father, "Presumed");

        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        var saved = await ReadAsync(client, family);
        Assert.Equal(father, saved!.Partner2Id);
        Assert.Equal(new Child(child, "Biological", "Presumed"), Assert.Single(saved.Children));
    }

    [Fact]
    public async Task The_first_partner_can_be_the_unknown_one()
    {
        var client = AdminClient();
        var (father, mother, child) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, null, father);
        await AddChildAsync(client, family, child);

        await NamePartnerAsync(client, family, mother);

        var saved = await ReadAsync(client, family);
        Assert.Equal(mother, saved!.Partner1Id);
        Assert.Equal("Biological", Assert.Single(saved.Children).Partner1Relationship);
    }

    [Fact]
    public async Task A_partner_cannot_be_named_when_it_would_break_the_tree()
    {
        var client = AdminClient();
        var (parent, child, grandchild, stranger) = (
            await PersonAsync(client), await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, parent);
        await AddChildAsync(client, family, child);
        await AddChildAsync(client, await FamilyAsync(client, child), grandchild);

        await AssertRefusedAsync(await NamePartnerAsync(client, family, parent), "partnerId");
        await AssertRefusedAsync(await NamePartnerAsync(client, family, child), "partnerId");
        await AssertRefusedAsync(await NamePartnerAsync(client, family, grandchild), "partnerId");
        await AssertRefusedAsync(await NamePartnerAsync(client, family, Guid.NewGuid()), "partnerId");
        Assert.Equal(HttpStatusCode.OK, (await NamePartnerAsync(client, family, stranger)).StatusCode);
        await AssertRefusedAsync(await NamePartnerAsync(client, family, await PersonAsync(client)), "partnerId");
    }

    [Fact]
    public async Task A_partnerships_type_can_be_corrected()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client), type: "Unknown");

        var changed = await client.PutAsJsonAsync($"{Families}/{family}", new { type = "Cohabiting" }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("Cohabiting", (await ReadAsync(client, family))!.Type);
    }

    [Fact]
    public async Task Removing_a_partner_leaves_the_children_with_the_other_partner()
    {
        var client = AdminClient();
        var (mother, wrongFather, child) = (await PersonAsync(client), await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, mother, wrongFather);
        await AddChildAsync(client, family, child);

        var removed = await client.DeleteAsync($"{Families}/{family}/partners/{wrongFather}", Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var saved = await ReadAsync(client, family);
        Assert.Equal(mother, saved!.Partner1Id);
        Assert.Null(saved.Partner2Id);
        Assert.Equal(new Child(child, "Biological", "Unknown"), Assert.Single(saved.Children));
    }

    [Fact]
    public async Task Removing_a_partner_from_a_family_with_no_children_removes_the_family()
    {
        var client = AdminClient();
        var (first, second) = (await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, first, second);

        await client.DeleteAsync($"{Families}/{family}/partners/{second}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Families}/{family}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{People}/{second}", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task The_only_known_parent_of_children_cannot_be_removed()
    {
        var client = AdminClient();
        var (parent, child) = (await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, parent);
        await AddChildAsync(client, family, child);

        var response = await client.DeleteAsync($"{Families}/{family}/partners/{parent}", Cancellation);

        await AssertRefusedAsync(response, "partnerId");
    }

    [Fact]
    public async Task Someone_who_is_not_a_partner_cannot_be_removed_as_one()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client), await PersonAsync(client));

        var response = await client.DeleteAsync($"{Families}/{family}/partners/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_change_partnerships()
    {
        var api = Api();
        var (admin, viewer) = (api.CreateClientFor(Admin), api.CreateClientFor(Viewer));
        var partner = await PersonAsync(admin);
        var family = await FamilyAsync(admin, await PersonAsync(admin), partner);

        var change = await viewer.PutAsJsonAsync($"{Families}/{family}", new { type = "None" }, Cancellation);
        var name = await NamePartnerAsync(viewer, family, partner);
        var remove = await viewer.DeleteAsync($"{Families}/{family}/partners/{partner}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, name.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
    }

    private static async Task<string> AddMarriageAsync(HttpClient client, Guid family, string date = "abt 1895", string? place = "Innellan")
    {
        var added = await client.PostAsJsonAsync($"{Families}/{family}/events", new { type = "Marriage", date, place }, Cancellation);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var saved = await added.Content.ReadFromJsonAsync<FamilyResponse>(Cancellation);
        return $"{Families}/{family}/events/{saved!.Events.Single(e => e.Type == "Marriage").Id}";
    }

    [Fact]
    public async Task A_marriage_is_recorded_with_a_fuzzy_date_and_a_place_and_shows_for_both_partners()
    {
        var client = AdminClient();
        var (first, second) = (await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, first, second);

        await AddMarriageAsync(client, family);

        foreach (var partner in new[] { first, second })
        {
            var families = await client.GetFromJsonAsync<PersonFamilies>($"{People}/{partner}/families", Cancellation);
            var marriage = Assert.Single(Assert.Single(families!.AsPartner).Events);
            Assert.Equal(("Marriage", "c. 1895", "abt 1895", "Innellan"), (marriage.Type, marriage.Date, marriage.DateAsEntered, marriage.Place));
        }
    }

    [Fact]
    public async Task A_familys_events_come_back_in_date_order_with_undated_ones_last()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));
        await client.PostAsJsonAsync($"{Families}/{family}/events", new { type = "Divorce", date = "1921" }, Cancellation);
        await AddMarriageAsync(client, family, "1 May 1895");
        var other = await FamilyAsync(client, await PersonAsync(client));
        await client.PostAsJsonAsync($"{Families}/{other}/events", new { type = "Divorce" }, Cancellation);
        await AddMarriageAsync(client, other, "1895");

        Assert.Equal(["Marriage", "Divorce"], (await ReadAsync(client, family))!.Events.Select(e => e.Type));
        Assert.Equal(["Marriage", "Divorce"], (await ReadAsync(client, other))!.Events.Select(e => e.Type));
    }

    [Fact]
    public async Task A_family_can_only_record_one_marriage()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));
        await AddMarriageAsync(client, family);

        var second = await client.PostAsJsonAsync($"{Families}/{family}/events", new { type = "Marriage" }, Cancellation);

        await AssertRefusedAsync(second, "type");
    }

    [Fact]
    public async Task A_marriage_date_that_makes_no_sense_is_refused_with_a_reason()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));

        var response = await client.PostAsJsonAsync($"{Families}/{family}/events", new { type = "Marriage", date = "31 Feb 1900" }, Cancellation);

        await AssertRefusedAsync(response, "date");
    }

    [Fact]
    public async Task A_place_too_long_to_store_is_refused_with_a_reason()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));

        var response = await client.PostAsJsonAsync($"{Families}/{family}/events", new { type = "Marriage", place = new string('x', 201) }, Cancellation);

        await AssertRefusedAsync(response, "place");
    }

    [Fact]
    public async Task A_marriage_can_be_corrected_and_rewording_keeps_what_was_typed()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));
        var marriage = await AddMarriageAsync(client, family);

        var changed = await client.PutAsJsonAsync(marriage, new { date = "circa 1895", place = "Dunoon" }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var saved = Assert.Single((await ReadAsync(client, family))!.Events);
        Assert.Equal(("circa 1895", "c. 1895", "Dunoon"), (saved.DateAsEntered, saved.Date, saved.Place));
    }

    [Fact]
    public async Task A_bad_correction_is_refused_and_keeps_the_old_date()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));
        var marriage = await AddMarriageAsync(client, family);

        var response = await client.PutAsJsonAsync(marriage, new { date = "31 Feb 1900" }, Cancellation);

        await AssertRefusedAsync(response, "date");
        Assert.Equal("c. 1895", Assert.Single((await ReadAsync(client, family))!.Events).Date);
    }

    [Fact]
    public async Task Removing_a_marriage_means_it_can_be_recorded_again()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));
        var marriage = await AddMarriageAsync(client, family);

        var removed = await client.DeleteAsync(marriage, Cancellation);
        var empty = await ReadAsync(client, family);
        await AddMarriageAsync(client, family, "1896");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(empty!.Events);
    }

    [Fact]
    public async Task An_event_can_only_be_reached_through_its_own_family()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));
        var marriage = await AddMarriageAsync(client, family);
        var other = await FamilyAsync(client, await PersonAsync(client));
        var wrongAddress = $"{Families}/{other}/events/{marriage.Split('/')[^1]}";

        var change = await client.PutAsJsonAsync(wrongAddress, new { date = "1999" }, Cancellation);
        var remove = await client.DeleteAsync(wrongAddress, Cancellation);
        var add = await client.PostAsJsonAsync($"{Families}/{Guid.NewGuid()}/events", new { type = "Marriage" }, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
        Assert.Equal("c. 1895", Assert.Single((await ReadAsync(client, family))!.Events).Date);
    }

    [Fact]
    public async Task Deleting_a_family_removes_its_events()
    {
        var client = AdminClient();
        var family = await FamilyAsync(client, await PersonAsync(client));
        await AddMarriageAsync(client, family);

        var deleted = await client.DeleteAsync($"{Families}/{family}", Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_change_a_familys_events()
    {
        var api = Api();
        var (admin, viewer) = (api.CreateClientFor(Admin), api.CreateClientFor(Viewer));
        var family = await FamilyAsync(admin, await PersonAsync(admin));
        var marriage = await AddMarriageAsync(admin, family);

        var add = await viewer.PostAsJsonAsync($"{Families}/{family}/events", new { type = "Divorce" }, Cancellation);
        var change = await viewer.PutAsJsonAsync(marriage, new { date = "1999" }, Cancellation);
        var remove = await viewer.DeleteAsync(marriage, Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_family_leaves_the_people_in_it()
    {
        var client = AdminClient();
        var (parent, child) = (await PersonAsync(client), await PersonAsync(client));
        var family = await FamilyAsync(client, parent);
        await AddChildAsync(client, family, child);

        var deleted = await client.DeleteAsync($"{Families}/{family}", Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Families}/{family}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{People}/{child}", Cancellation)).StatusCode);
        var families = await client.GetFromJsonAsync<PersonFamilies>($"{People}/{child}/families", Cancellation);
        Assert.Empty(families!.AsChild);
    }

    [Fact]
    public async Task Things_that_do_not_exist_get_not_found()
    {
        var client = AdminClient();
        var person = await PersonAsync(client);
        var family = await FamilyAsync(client, person);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Families}/{Guid.NewGuid()}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddChildAsync(client, Guid.NewGuid(), person)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Families}/{family}/children/{Guid.NewGuid()}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{People}/{Guid.NewGuid()}/families", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_read_or_change_families_here()
    {
        var client = Api().CreateClientFor(Viewer);

        var add = await client.PostAsJsonAsync(Families, new { partner1Id = Guid.NewGuid() }, Cancellation);
        var read = await client.GetAsync($"{Families}/{Guid.NewGuid()}", Cancellation);
        var forPerson = await client.GetAsync($"{People}/{Guid.NewGuid()}/families", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forPerson.StatusCode);
    }
}
