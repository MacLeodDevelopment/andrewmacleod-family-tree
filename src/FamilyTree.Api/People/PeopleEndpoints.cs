using FamilyTree.Api.Authorization;
using FamilyTree.Domain;
using FamilyTree.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.People;

public static class PeopleEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public sealed record NameRequest(string? GivenName, string? Surname, NameType Type = NameType.Birth);

    public sealed record CreatePersonRequest(IReadOnlyList<NameRequest>? Names, Sex Sex = Sex.Unknown);

    public sealed record NameResponse(string GivenName, string? Surname, NameType Type);

    public sealed record AddEventRequest(EventType Type, string? Date, string? Place);

    public sealed record EventResponse(Guid Id, EventType Type, string? Date, string? DateAsEntered, string? Place);

    public sealed record PersonResponse(Guid Id, Sex Sex, IReadOnlyList<NameResponse> Names, IReadOnlyList<EventResponse> Events);

    public sealed record PeoplePage(IReadOnlyList<PersonResponse> Items, int Total);

    /// <summary>
    /// /admin/people: creating and reading people. For admins only, because viewers will
    /// see people through endpoints that apply the privacy rules.
    /// </summary>
    public static RouteGroupBuilder MapPeopleEndpoints(this RouteGroupBuilder group)
    {
        var people = group.MapGroup("/admin/people").RequireAuthorization(FamilyPolicies.Admin);

        people.MapPost("/", CreateAsync);
        people.MapGet("/{id:guid}", GetAsync).WithName(nameof(GetAsync));
        people.MapGet("/", ListAsync);
        people.MapDelete("/{id:guid}", DeleteAsync);
        people.MapPost("/{id:guid}/events", AddEventAsync);

        return group;
    }

    private static async Task<IResult> CreateAsync(
        CreatePersonRequest request, FamilyTreeDbContext database, CancellationToken cancellationToken, bool allowDuplicate = false)
    {
        var names = (request.Names ?? []).Select(name => new NameDetails(name.GivenName, name.Surname, name.Type)).ToList();
        var result = Person.Create(request.Sex, names);
        if (!result.IsSuccess)
        {
            return Results.ValidationProblem(result.Errors.ToDictionary());
        }

        if (!allowDuplicate && await FindSameNameAsync(result.Value, database, cancellationToken) is { Count: > 0 } matches)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This may be a duplicate",
                detail: "Someone with this name already exists. If this is a different person, send the request again with ?allowDuplicate=true.",
                extensions: new Dictionary<string, object?> { ["duplicates"] = matches });
        }

        database.People.Add(result.Value);
        await database.SaveChangesAsync(cancellationToken);

        return Results.CreatedAtRoute(nameof(GetAsync), new { id = result.Value.Id }, ToResponse(result.Value));
    }

    /// <summary>
    /// People whose names include this person's main name. It's a prompt, not a rule: families
    /// reuse names, so two people with the same name are often different people. That's also
    /// why the database has no unique constraint, and two identical requests sent at the same
    /// moment could both succeed.
    /// </summary>
    private static async Task<List<Guid>> FindSameNameAsync(
        Person person, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var (givenName, surname) = (person.Names[0].GivenName, person.Names[0].Surname);

        return await database.People
            .Where(existing => existing.Names.Any(name => name.GivenName == givenName && name.Surname == surname))
            .Select(existing => existing.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Permanently removes a person, with their names and events, unless they're in a family.</summary>
    private static async Task<IResult> DeleteAsync(Guid id, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var person = await database.People.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return Results.NotFound();
        }

        // Deleting someone mustn't quietly tear a hole in the tree.
        var inAFamily = await database.Families.AnyAsync(
            family => family.Partner1Id == id || family.Partner2Id == id || family.Children.Any(child => child.ChildId == id),
            cancellationToken);
        if (inAFamily)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This person is part of a family",
                detail: "Remove them from their families first.");
        }

        database.People.Remove(person);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> GetAsync(Guid id, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var person = await database.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        return person is null ? Results.NotFound() : Results.Ok(ToResponse(person));
    }

    private static async Task<PeoplePage> ListAsync(
        FamilyTreeDbContext database, CancellationToken cancellationToken, int skip = 0, int take = DefaultPageSize)
    {
        var people = await database.People.AsNoTracking()
            .OrderBy(person => person.Id)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return new PeoplePage(
            people.Select(ToResponse).ToList(),
            await database.People.CountAsync(cancellationToken));
    }

    private static async Task<IResult> AddEventAsync(
        Guid id, AddEventRequest request, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var person = await database.People.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return Results.NotFound();
        }

        var result = person.AddEvent(request.Type, request.Date, request.Place);
        if (!result.IsSuccess)
        {
            return Results.ValidationProblem(result.Errors.ToDictionary());
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.CreatedAtRoute(nameof(GetAsync), new { id }, ToResponse(person));
    }

    private static PersonResponse ToResponse(Person person) => new(
        person.Id,
        person.Sex,
        person.Names.OrderBy(name => name.Position)
            .Select(name => new NameResponse(name.GivenName, name.Surname, name.Type))
            .ToList(),
        person.Timeline
            .Select(lifeEvent => new EventResponse(
                lifeEvent.Id, lifeEvent.Type, lifeEvent.Date?.Display, lifeEvent.Date?.OriginalText, lifeEvent.Place))
            .ToList());
}
