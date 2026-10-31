using FamilyTree.Api.People;
using FamilyTree.Domain;
using FamilyTree.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.Profiles;

public static class ProfilesEndpoints
{
    public const int MaxSearchResults = 20;

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

    public sealed record Profile(
        Guid Id,
        Sex Sex,
        IReadOnlyList<ProfileName> Names,
        IReadOnlyList<ProfileEvent> Events,
        IReadOnlyList<ProfileParent> Parents,
        IReadOnlyList<ProfileSibling> Siblings,
        IReadOnlyList<ProfilePartnership> Partnerships);

    /// <summary>
    /// /people: finding and reading people, for every family member. These have their own
    /// response types, so what relatives can see is decided here and nowhere else, and
    /// adding to the admin screens can't show them anything new.
    /// </summary>
    public static RouteGroupBuilder MapProfilesEndpoints(this RouteGroupBuilder group)
    {
        var people = group.MapGroup("/people");

        people.MapGet("/search", SearchAsync);
        people.MapGet("/{id:guid}", GetAsync);

        return group;
    }

    private static async Task<SearchResults> SearchAsync(string? q, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return new SearchResults([], 0);
        }

        var matching = database.People.AsNoTracking().WithNameContaining(q);
        var people = await matching.OrderBy(person => person.Id).Take(MaxSearchResults).ToListAsync(cancellationToken);

        return new SearchResults(
            people.Select(ToSummary).OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            await matching.CountAsync(cancellationToken));
    }

    private static async Task<IResult> GetAsync(Guid id, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var person = await database.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return Results.NotFound();
        }

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
        var summaries = (await database.People.AsNoTracking()
            .Where(p => everyoneMentioned.Contains(p.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, ToSummary);

        return Results.Ok(new Profile(
            person.Id,
            person.Sex,
            person.Names.OrderBy(name => name.Position).Select(name => new ProfileName(name.GivenName, name.Surname, name.Type)).ToList(),
            person.Timeline.Select(e => new ProfileEvent(e.Type.ToString(), e.Date?.Display, e.Place)).ToList(),
            asChild.SelectMany(f => ParentsIn(f, id, summaries)).ToList(),
            siblings.Select(sibling => new ProfileSibling(summaries[sibling], IsHalf: false))
                .Concat(halfSiblings.Select(sibling => new ProfileSibling(summaries[sibling], IsHalf: true)))
                .ToList(),
            asPartner.Select(f => ToPartnership(f, id, summaries)).ToList()));
    }

    private static IEnumerable<ProfileParent> ParentsIn(Family family, Guid childId, Dictionary<Guid, PersonSummary> summaries)
    {
        var link = family.Children.Single(child => child.ChildId == childId);

        yield return new ProfileParent(Summary(family.Partner1Id, summaries), link.Partner1Relationship);
        yield return new ProfileParent(Summary(family.Partner2Id, summaries), link.Partner2Relationship);
    }

    private static ProfilePartnership ToPartnership(Family family, Guid personId, Dictionary<Guid, PersonSummary> summaries)
    {
        var isPartner1 = family.Partner1Id == personId;

        return new ProfilePartnership(
            Summary(isPartner1 ? family.Partner2Id : family.Partner1Id, summaries),
            family.Type,
            family.Timeline.Select(e => new ProfileEvent(e.Type.ToString(), e.Date?.Display, e.Place)).ToList(),
            family.Children.OrderBy(child => child.Position)
                .Select(child => new ProfileChild(
                    summaries[child.ChildId],
                    isPartner1 ? child.Partner1Relationship : child.Partner2Relationship))
                .ToList());
    }

    private static PersonSummary? Summary(Guid? id, Dictionary<Guid, PersonSummary> summaries) =>
        id is null ? null : summaries[id.Value];

    private static PersonSummary ToSummary(Person person)
    {
        var main = person.Names.OrderBy(name => name.Position).First();

        return new PersonSummary(
            person.Id,
            string.Join(' ', new[] { main.GivenName, main.Surname }.Where(part => part is not null)),
            DateOf(person, EventType.Birth),
            DateOf(person, EventType.Death));
    }

    private static string? DateOf(Person person, EventType type) =>
        person.Events.FirstOrDefault(lifeEvent => lifeEvent.Type == type)?.Date?.Display;
}
