using System.Security.Claims;
using FamilyTree.Api.Account;
using FamilyTree.Api.Authorization;
using FamilyTree.Api.People;
using FamilyTree.Domain;
using FamilyTree.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.Profiles;

public static class ProfilesEndpoints
{
    public const int MaxSearchResults = 20;
    public const int DefaultGenerationsUp = 3;
    public const int DefaultGenerationsDown = 2;
    public const int MaxGenerations = 6;

    /// <summary>Enough to recognise someone and tell them from others with the same name.</summary>
    public sealed record PersonSummary(Guid Id, string Name, string? Born, string? Died);

    public sealed record SearchResults(IReadOnlyList<PersonSummary> Items, int Total);

    public sealed record ProfileName(string GivenName, string? Surname, NameType Type);

    public sealed record ProfileEvent(string Type, string? Date, string? Place);

    /// <summary>A parent, or a parent's unknown place in a family when <see cref="Person"/> is null.</summary>
    public sealed record ProfileParent(PersonSummary? Person, ParentRelationship Relationship);

    public sealed record ProfileSibling(PersonSummary Person, bool IsHalf);

    public sealed record ProfileChild(PersonSummary Person, ParentRelationship Relationship);

    public sealed record ProfilePartnership(
        PersonSummary? Partner,
        PartnershipType Type,
        IReadOnlyList<ProfileEvent> Events,
        IReadOnlyList<ProfileChild> Children);

    /// <param name="IsPrivate">True if their details are hidden because they may be living.</param>
    public sealed record Profile(
        Guid Id,
        bool IsPrivate,
        IReadOnlyList<ProfileName> Names,
        IReadOnlyList<ProfileEvent> Events,
        IReadOnlyList<ProfileParent> Parents,
        IReadOnlyList<ProfileSibling> Siblings,
        IReadOnlyList<ProfilePartnership> Partnerships);

    /// <summary>A person in a graph. Born and Died follow the same privacy rule as everywhere else.</summary>
    public sealed record GraphPerson(Guid Id, string Name, string? Born, string? Died, bool IsHalfSibling);

    public sealed record GraphChild(Guid ChildId, ParentRelationship Partner1Relationship, ParentRelationship Partner2Relationship);

    public sealed record GraphFamily(Guid Id, Guid? Partner1Id, Guid? Partner2Id, PartnershipType Type, IReadOnlyList<GraphChild> Children);

    /// <summary>
    /// The tree around someone, as plain data. It says who is related to whom and leaves
    /// where to draw them to the client. It has no events, so no marriage dates to protect.
    /// </summary>
    public sealed record Graph(Guid FocusId, IReadOnlyList<GraphPerson> People, IReadOnlyList<GraphFamily> Families);

    /// <summary>
    /// /people: finding and reading people, for every family member. These have their own
    /// response types, so what relatives can see is decided here and nowhere else, and
    /// adding to the admin screens can't show them anything new. Everything about a person
    /// that isn't their name or a relationship goes through <see cref="Privacy"/>.
    /// </summary>
    public static RouteGroupBuilder MapProfilesEndpoints(this RouteGroupBuilder group)
    {
        var people = group.MapGroup("/people");

        people.MapGet("/search", SearchAsync);
        people.MapGet("/{id:guid}", GetAsync);
        people.MapGet("/{id:guid}/graph", GraphAsync);

        return group;
    }

    /// <summary>
    /// Whose details this reader can't see: anyone who may be living, except that admins
    /// see everyone, and someone approved as a person sees that person.
    /// </summary>
    private sealed record Privacy(bool SeesEverything, Guid? OwnPerson, int CurrentYear)
    {
        public static async Task<Privacy> ForAsync(
            ClaimsPrincipal user, IAuthorizationService authorization, FamilyTreeDbContext database, CancellationToken cancellationToken)
        {
            var result = await authorization.AuthorizeAsync(user, FamilyPolicies.Admin);
            var ownPerson = await AccountLinksEndpoints.ApprovedPersonAsync(user, database, cancellationToken);
            return new Privacy(result.Succeeded, ownPerson, TimeProvider.System.GetUtcNow().Year);
        }

        public bool Hides(Person person) =>
            !SeesEverything && person.Id != OwnPerson && person.MayBeLiving(CurrentYear);

        /// <summary>A marriage is private if either partner's details are, but it's the reader's own if they're one of them.</summary>
        public bool ShowsEventsOf(Family family, Dictionary<Guid, Person> relatives) =>
            family.Partners.Any(partner => partner == OwnPerson)
            || !family.Partners.Any(partner => Hides(relatives[partner]));
    }

    private static async Task<SearchResults> SearchAsync(
        string? q, ClaimsPrincipal user, IAuthorizationService authorization, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return new SearchResults([], 0);
        }

        var privacy = await Privacy.ForAsync(user, authorization, database, cancellationToken);
        var matching = database.People.AsNoTracking().WithNameContaining(q);
        var people = await matching.OrderBy(person => person.Id).Take(MaxSearchResults).ToListAsync(cancellationToken);

        return new SearchResults(
            people.Select(person => ToSummary(person, privacy)).OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            await matching.CountAsync(cancellationToken));
    }

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal user, IAuthorizationService authorization, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var person = await database.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return Results.NotFound();
        }

        var privacy = await Privacy.ForAsync(user, authorization, database, cancellationToken);
        var families = await database.Families.AsNoTracking()
            .Where(f => f.Partner1Id == id || f.Partner2Id == id || f.Children.Any(child => child.ChildId == id))
            .ToListAsync(cancellationToken);
        var asChild = families.Where(f => f.Children.Any(child => child.ChildId == id)).ToList();
        var asPartner = families.Where(f => f.Partners.Contains(id)).ToList();

        // A parent's other families hold the half-siblings, which are never recorded as such.
        var parents = asChild.SelectMany(f => f.Partners).Cast<Guid?>().Distinct().ToList();
        var ownFamilies = asChild.Select(f => f.Id).ToList();
        var parentsOtherFamilies = await database.Families.AsNoTracking()
            .Where(f => (parents.Contains(f.Partner1Id) || parents.Contains(f.Partner2Id)) && !ownFamilies.Contains(f.Id))
            .ToListAsync(cancellationToken);

        var siblings = asChild.SelectMany(f => f.Children).Select(child => child.ChildId).Where(child => child != id).Distinct().ToList();
        var halfSiblings = parentsOtherFamilies.SelectMany(f => f.Children).Select(child => child.ChildId)
            .Except(siblings).Distinct().ToList();

        var everyoneMentioned = families.Concat(parentsOtherFamilies)
            .SelectMany(f => f.Partners.Concat(f.Children.Select(child => child.ChildId)))
            .Distinct().ToList();
        var relatives = (await database.People.AsNoTracking()
            .Where(p => everyoneMentioned.Contains(p.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id);
        var summaries = relatives.Values.ToDictionary(p => p.Id, p => ToSummary(p, privacy));
        var isPrivate = privacy.Hides(person);

        return Results.Ok(new Profile(
            person.Id,
            isPrivate,
            person.Names.OrderBy(name => name.Position).Select(name => new ProfileName(name.GivenName, name.Surname, name.Type)).ToList(),
            isPrivate ? [] : person.Timeline.Select(e => new ProfileEvent(e.Type.ToString(), e.Date?.Display, e.Place)).ToList(),
            asChild.SelectMany(f => ParentsIn(f, id, summaries)).ToList(),
            siblings.Select(sibling => new ProfileSibling(summaries[sibling], IsHalf: false))
                .Concat(halfSiblings.Select(sibling => new ProfileSibling(summaries[sibling], IsHalf: true)))
                .ToList(),
            asPartner.Select(f => ToPartnership(f, id, summaries, privacy.ShowsEventsOf(f, relatives))).ToList()));
    }

    private static async Task<IResult> GraphAsync(
        Guid id,
        ClaimsPrincipal user,
        IAuthorizationService authorization,
        FamilyTreeDbContext database,
        CancellationToken cancellationToken,
        int up = DefaultGenerationsUp,
        int down = DefaultGenerationsDown)
    {
        if (!await database.People.AnyAsync(person => person.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var privacy = await Privacy.ForAsync(user, authorization, database, cancellationToken);
        var tree = new FamilyGraph(await database.Families.AsNoTracking().ToListAsync(cancellationToken));
        var around = tree.Around(id, Math.Clamp(up, 0, MaxGenerations), Math.Clamp(down, 0, MaxGenerations));

        var everyone = around.Families
            .SelectMany(f => f.Partners.Concat(f.Children.Select(child => child.ChildId)))
            .Append(id)
            .Distinct().ToList();
        var people = await database.People.AsNoTracking()
            .Where(person => everyone.Contains(person.Id))
            .ToListAsync(cancellationToken);

        return Results.Ok(new Graph(
            id,
            people.Select(person => ToSummary(person, privacy))
                .Select(p => new GraphPerson(p.Id, p.Name, p.Born, p.Died, around.HalfSiblings.Contains(p.Id)))
                .ToList(),
            around.Families
                .Select(f => new GraphFamily(
                    f.Id,
                    f.Partner1Id,
                    f.Partner2Id,
                    f.Type,
                    f.Children.OrderBy(child => child.Position)
                        .Select(child => new GraphChild(child.ChildId, child.Partner1Relationship, child.Partner2Relationship))
                        .ToList()))
                .ToList()));
    }

    private static IEnumerable<ProfileParent> ParentsIn(Family family, Guid childId, Dictionary<Guid, PersonSummary> summaries)
    {
        var link = family.Children.Single(child => child.ChildId == childId);

        yield return new ProfileParent(Summary(family.Partner1Id, summaries), link.Partner1Relationship);
        yield return new ProfileParent(Summary(family.Partner2Id, summaries), link.Partner2Relationship);
    }

    private static ProfilePartnership ToPartnership(
        Family family, Guid personId, Dictionary<Guid, PersonSummary> summaries, bool showsEvents)
    {
        var isPartner1 = family.Partner1Id == personId;

        return new ProfilePartnership(
            Summary(isPartner1 ? family.Partner2Id : family.Partner1Id, summaries),
            family.Type,
            showsEvents ? family.Timeline.Select(e => new ProfileEvent(e.Type.ToString(), e.Date?.Display, e.Place)).ToList() : [],
            family.Children.OrderBy(child => child.Position)
                .Select(child => new ProfileChild(
                    summaries[child.ChildId],
                    isPartner1 ? child.Partner1Relationship : child.Partner2Relationship))
                .ToList());
    }

    private static PersonSummary? Summary(Guid? id, Dictionary<Guid, PersonSummary> summaries) =>
        id is null ? null : summaries[id.Value];

    private static PersonSummary ToSummary(Person person, Privacy privacy)
    {
        var main = person.Names.OrderBy(name => name.Position).First();
        var hidden = privacy.Hides(person);

        return new PersonSummary(
            person.Id,
            string.Join(' ', new[] { main.GivenName, main.Surname }.Where(part => part is not null)),
            hidden ? null : DateOf(person, EventType.Birth),
            hidden ? null : DateOf(person, EventType.Death));
    }

    private static string? DateOf(Person person, EventType type) =>
        person.Events.FirstOrDefault(lifeEvent => lifeEvent.Type == type)?.Date?.Display;
}
