using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace FamilyTree.Api.Tests;

public class PeopleTests(ApiFactory factory, SqlServerContainer sqlServer) : IClassFixture<ApiFactory>
{
    private const string People = "/api/v1/admin/people";
    private const string ProblemJson = "application/problem+json";

    private static readonly TestUser Admin = new("Andrew", "andrew@example.test", ["Family.Admin"]);
    private static readonly TestUser Viewer = new("Jean", "jean@example.test", ["Family.Viewer"]);

    private sealed record Name(string GivenName, string? Surname, string Type);

    private sealed record Event(string Type, string? Date, string? DateAsEntered, string? Place);

    private sealed record EventWithId(Guid Id);

    private sealed record PersonEvents(EventWithId[] Events);

    private sealed record PersonResponse(Guid Id, string Sex, Name[] Names, Event[] Events);

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

    /// <summary>Adds someone to attach events to, and returns the address of their record.</summary>
    private static async Task<string> AddPersonAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = UniqueName() } } }, Cancellation);
        return created.Headers.Location!.ToString();
    }

    /// <summary>The tests share a database, and a repeated name would count as a possible duplicate.</summary>
    private static string UniqueName() => $"Ewan {Guid.NewGuid():N}";

    [Fact]
    public async Task Adding_someone_who_may_already_exist_is_stopped_and_says_who_matches()
    {
        var client = Api().CreateClientFor(Admin);
        var person = new { names = new[] { new { givenName = UniqueName(), surname = "MacLeod" } } };
        var first = await client.PostAsJsonAsync(People, person, Cancellation);
        var existing = await first.Content.ReadFromJsonAsync<PersonResponse>(Cancellation);

        var second = await client.PostAsJsonAsync(People, person, Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ProblemJson, second.Content.Headers.ContentType?.MediaType);
        Assert.Contains(existing!.Id.ToString(), await second.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task Matching_a_name_ignores_case_and_stray_spaces()
    {
        var client = Api().CreateClientFor(Admin);
        var name = UniqueName();
        await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = name, surname = "MacLeod" } } }, Cancellation);

        var second = await client.PostAsJsonAsync(People,
            new { names = new[] { new { givenName = $"  {name.ToUpperInvariant()} ", surname = "macleod" } } }, Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Someone_who_shares_a_name_can_still_be_added_on_purpose()
    {
        var client = Api().CreateClientFor(Admin);
        var person = new { names = new[] { new { givenName = UniqueName(), surname = "MacLeod" } } };
        await client.PostAsJsonAsync(People, person, Cancellation);

        var second = await client.PostAsJsonAsync($"{People}?allowDuplicate=true", person, Cancellation);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task People_with_the_same_given_name_but_a_different_surname_are_not_duplicates()
    {
        var client = Api().CreateClientFor(Admin);
        var name = UniqueName();
        await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = name, surname = "MacLeod" } } }, Cancellation);

        var second = await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = name, surname = "Campbell" } } }, Cancellation);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Someone_known_by_a_married_name_is_matched_on_that_too()
    {
        var client = Api().CreateClientFor(Admin);
        var name = UniqueName();
        await client.PostAsJsonAsync(People, new
        {
            names = new[]
            {
                new { givenName = name, surname = "Campbell", type = "Birth" },
                new { givenName = name, surname = "MacLeod", type = "Married" },
            },
        }, Cancellation);

        var second = await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = name, surname = "MacLeod" } } }, Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_delete_a_person_with_their_names_and_events()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        await client.PostAsJsonAsync($"{person}/events", new { type = "Birth", date = "1870" }, Cancellation);

        var deleted = await client.DeleteAsync(person, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(person, Cancellation)).StatusCode);
        await using var connection = new SqlConnection(sqlServer.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var leftovers = connection.CreateCommand();
        leftovers.CommandText = "SELECT (SELECT COUNT(*) FROM PersonNames WHERE PersonId = @id) + (SELECT COUNT(*) FROM PersonEvents WHERE PersonId = @id)";
        leftovers.Parameters.AddWithValue("@id", Guid.Parse(person.Split('/')[^1]));
        Assert.Equal(0, await leftovers.ExecuteScalarAsync(Cancellation));
    }

    [Fact]
    public async Task Deleting_someone_who_does_not_exist_gets_not_found()
    {
        var response = await Api().CreateClientFor(Admin).DeleteAsync($"{People}/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_delete_people()
    {
        var api = Api();
        var person = await AddPersonAsync(api.CreateClientFor(Admin));

        var response = await api.CreateClientFor(Viewer).DeleteAsync(person, Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClientFor(Admin).GetAsync(person, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task An_admin_can_record_a_birth_with_a_fuzzy_date_and_a_place()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);

        var added = await client.PostAsJsonAsync($"{person}/events",
            new { type = "Birth", date = "abt 1870", place = "Innellan, Argyll" }, Cancellation);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var saved = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.Equal(new Event("Birth", "c. 1870", "abt 1870", "Innellan, Argyll"), Assert.Single(saved!.Events));
    }

    [Fact]
    public async Task Events_come_back_in_date_order_with_undated_ones_last()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);

        await client.PostAsJsonAsync($"{person}/events", new { type = "Death", date = "bef 1901" }, Cancellation);
        var birth = await client.PostAsJsonAsync($"{person}/events", new { type = "Birth", date = "1 May 1850" }, Cancellation);
        var saved = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        var other = await AddPersonAsync(client);
        await client.PostAsJsonAsync($"{other}/events", new { type = "Death" }, Cancellation);
        await client.PostAsJsonAsync($"{other}/events", new { type = "Birth", date = "1850" }, Cancellation);
        var undatedLast = await client.GetFromJsonAsync<PersonResponse>(other, Cancellation);

        Assert.Equal(HttpStatusCode.Created, birth.StatusCode);
        Assert.Equal(["Birth", "Death"], saved!.Events.Select(e => e.Type));
        Assert.Equal(["Birth", "Death"], undatedLast!.Events.Select(e => e.Type));
        Assert.Null(undatedLast.Events[1].Date);
    }

    [Fact]
    public async Task A_date_that_makes_no_sense_is_refused_with_a_reason()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);

        var response = await client.PostAsJsonAsync($"{person}/events", new { type = "Birth", date = "31 Feb 1900" }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Cancellation);
        Assert.Contains("date", problem!.Errors.Keys);
    }

    [Fact]
    public async Task A_person_can_only_be_born_once()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);

        await client.PostAsJsonAsync($"{person}/events", new { type = "Birth", date = "1870" }, Cancellation);
        var second = await client.PostAsJsonAsync($"{person}/events", new { type = "Birth", date = "1871" }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Events_cannot_be_added_to_someone_who_does_not_exist()
    {
        var response = await Api().CreateClientFor(Admin).PostAsJsonAsync(
            $"{People}/{Guid.NewGuid()}/events", new { type = "Birth" }, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_add_events()
    {
        var response = await Api().CreateClientFor(Viewer).PostAsJsonAsync(
            $"{People}/{Guid.NewGuid()}/events", new { type = "Birth" }, Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Records a birth on someone, and returns the address of the event.</summary>
    private static async Task<string> AddBirthAsync(HttpClient client, string person, string date = "abt 1870", string? place = "Innellan")
    {
        var added = await client.PostAsJsonAsync($"{person}/events", new { type = "Birth", date, place }, Cancellation);
        var saved = await added.Content.ReadFromJsonAsync<PersonEvents>(Cancellation);
        return $"{person}/events/{saved!.Events.Single().Id}";
    }

    [Fact]
    public async Task An_admin_can_correct_a_persons_sex_and_name()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);

        var updated = await client.PutAsJsonAsync(person,
            new { sex = "Female", names = new[] { new { givenName = "Mòrag", surname = "MacLeod", type = "Birth" } } }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.Equal("Female", saved!.Sex);
        Assert.Equal(new Name("Mòrag", "MacLeod", "Birth"), Assert.Single(saved.Names));
    }

    [Fact]
    public async Task Names_can_be_added_and_removed_over_several_changes()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        var givenName = UniqueName();
        var birth = new { givenName, surname = "Campbell", type = "Birth" };
        var married = new { givenName, surname = "MacLeod", type = "Married" };
        var nickname = new { givenName = "Izzy", surname = (string?)null, type = "Nickname" };

        await client.PutAsJsonAsync(person, new { names = new[] { birth, married, nickname } }, Cancellation);
        var three = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        await client.PutAsJsonAsync(person, new { names = new[] { married } }, Cancellation);
        var one = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        await client.PutAsJsonAsync(person, new { names = new[] { married, birth } }, Cancellation);
        var two = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);

        Assert.Equal(["Campbell", "MacLeod", null], three!.Names.Select(name => name.Surname));
        Assert.Equal(["MacLeod"], one!.Names.Select(name => name.Surname));
        Assert.Equal(["MacLeod", "Campbell"], two!.Names.Select(name => name.Surname));
    }

    [Fact]
    public async Task Changing_a_person_keeps_their_events()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        await AddBirthAsync(client, person);

        await client.PutAsJsonAsync(person, new { names = new[] { new { givenName = UniqueName() } } }, Cancellation);

        var saved = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.Equal("c. 1870", Assert.Single(saved!.Events).Date);
    }

    [Fact]
    public async Task A_change_that_breaks_the_rules_is_refused_and_changes_nothing()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        var before = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);

        var response = await client.PutAsJsonAsync(person,
            new { sex = "Male", names = new[] { new { givenName = " " } } }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Cancellation);
        Assert.Contains("names[0].givenName", problem!.Errors.Keys);
        var after = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.Equal("Unknown", after!.Sex);
        Assert.Equal(before!.Names, after.Names);
    }

    [Fact]
    public async Task Correcting_someone_to_a_name_already_in_use_is_allowed()
    {
        var client = Api().CreateClientFor(Admin);
        var name = UniqueName();
        await client.PostAsJsonAsync(People, new { names = new[] { new { givenName = name } } }, Cancellation);
        var person = await AddPersonAsync(client);

        var response = await client.PutAsJsonAsync(person, new { names = new[] { new { givenName = name } } }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_correct_when_and_where_an_event_happened()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        var birth = await AddBirthAsync(client, person);

        var changed = await client.PutAsJsonAsync(birth, new { date = "12 Mar 1871", place = "Dunoon" }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var saved = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.Equal(new Event("Birth", "12 March 1871", "12 Mar 1871", "Dunoon"), Assert.Single(saved!.Events));
    }

    [Fact]
    public async Task Rewording_a_date_keeps_what_was_typed()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        var birth = await AddBirthAsync(client, person);

        await client.PutAsJsonAsync(birth, new { date = "circa 1870" }, Cancellation);

        var saved = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.Equal("circa 1870", Assert.Single(saved!.Events).DateAsEntered);
    }

    [Fact]
    public async Task A_date_and_place_can_be_cleared_when_they_turn_out_to_be_wrong()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        var birth = await AddBirthAsync(client, person);

        await client.PutAsJsonAsync(birth, new { date = "", place = " " }, Cancellation);

        var saved = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.Equal(new Event("Birth", null, null, null), Assert.Single(saved!.Events));
    }

    [Fact]
    public async Task A_bad_date_on_an_event_is_refused_and_keeps_the_old_one()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        var birth = await AddBirthAsync(client, person);

        var response = await client.PutAsJsonAsync(birth, new { date = "31 Feb 1900", place = "Dunoon" }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Cancellation);
        Assert.Contains("date", problem!.Errors.Keys);
        var saved = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.Equal(new Event("Birth", "c. 1870", "abt 1870", "Innellan"), Assert.Single(saved!.Events));
    }

    [Fact]
    public async Task Removing_an_event_means_it_can_be_recorded_again()
    {
        var client = Api().CreateClientFor(Admin);
        var person = await AddPersonAsync(client);
        var birth = await AddBirthAsync(client, person);

        var removed = await client.DeleteAsync(birth, Cancellation);
        var empty = await client.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        var again = await client.PostAsJsonAsync($"{person}/events", new { type = "Birth", date = "1871" }, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(empty!.Events);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Changing_someone_who_does_not_exist_gets_not_found()
    {
        var response = await Api().CreateClientFor(Admin).PutAsJsonAsync(
            $"{People}/{Guid.NewGuid()}", new { names = new[] { new { givenName = "Mairi" } } }, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_event_can_only_be_reached_through_the_person_it_belongs_to()
    {
        var client = Api().CreateClientFor(Admin);
        var owner = await AddPersonAsync(client);
        var birth = await AddBirthAsync(client, owner);
        var someoneElse = await AddPersonAsync(client);
        var wrongAddress = $"{someoneElse}/events/{birth.Split('/')[^1]}";

        var change = await client.PutAsJsonAsync(wrongAddress, new { date = "1999" }, Cancellation);
        var remove = await client.DeleteAsync(wrongAddress, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        var saved = await client.GetFromJsonAsync<PersonResponse>(owner, Cancellation);
        Assert.Equal("c. 1870", Assert.Single(saved!.Events).Date);
    }

    [Fact]
    public async Task Viewers_cannot_change_people_or_events()
    {
        var api = Api();
        var admin = api.CreateClientFor(Admin);
        var person = await AddPersonAsync(admin);
        var birth = await AddBirthAsync(admin, person);
        var viewer = api.CreateClientFor(Viewer);

        var update = await viewer.PutAsJsonAsync(person, new { names = new[] { new { givenName = "Hacked" } } }, Cancellation);
        var change = await viewer.PutAsJsonAsync(birth, new { date = "1999" }, Cancellation);
        var remove = await viewer.DeleteAsync(birth, Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        var saved = await admin.GetFromJsonAsync<PersonResponse>(person, Cancellation);
        Assert.NotEqual("Hacked", saved!.Names[0].GivenName);
        Assert.Equal("c. 1870", Assert.Single(saved.Events).Date);
    }

    [Fact]
    public async Task Visitors_who_have_not_signed_in_are_refused()
    {
        var response = await Api().CreateClient().GetAsync(People, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
