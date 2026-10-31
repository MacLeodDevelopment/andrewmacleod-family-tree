using FamilyTree.Api.Authorization;
using FamilyTree.Api.People;
using FamilyTree.Domain;
using FamilyTree.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.Families;

public static class FamiliesEndpoints
{
    public sealed record AddChildRequest(
        Guid ChildId,
        ParentRelationship Partner1Relationship = ParentRelationship.Biological,
        ParentRelationship Partner2Relationship = ParentRelationship.Biological);

    public sealed record CreateFamilyRequest(
        Guid? Partner1Id,
        Guid? Partner2Id,
        PartnershipType Type = PartnershipType.Unknown,
        IReadOnlyList<AddChildRequest>? Children = null);

    public sealed record ChildResponse(Guid ChildId, ParentRelationship Partner1Relationship, ParentRelationship Partner2Relationship);

    public sealed record FamilyResponse(Guid Id, Guid? Partner1Id, Guid? Partner2Id, PartnershipType Type, IReadOnlyList<ChildResponse> Children);

    /// <summary>A person's families, with everyone in them, so a screen can name them without more requests.</summary>
    public sealed record PersonFamiliesResponse(
        IReadOnlyList<FamilyResponse> AsChild,
        IReadOnlyList<FamilyResponse> AsPartner,
        IReadOnlyList<PeopleEndpoints.PersonResponse> People);

    /// <summary>/admin/families and /admin/people/{id}/families: recording how people are related.</summary>
    public static RouteGroupBuilder MapFamiliesEndpoints(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin").RequireAuthorization(FamilyPolicies.Admin);

        admin.MapPost("/families", CreateAsync);
        admin.MapGet("/families/{id:guid}", GetAsync).WithName(nameof(FamiliesEndpoints) + nameof(GetAsync));
        admin.MapDelete("/families/{id:guid}", DeleteAsync);
        admin.MapPost("/families/{id:guid}/children", AddChildAsync);
        admin.MapDelete("/families/{id:guid}/children/{childId:guid}", RemoveChildAsync);
        admin.MapGet("/people/{id:guid}/families", ForPersonAsync);

        return group;
    }

    /// <summary>
    /// Records a family, with any children given. They're saved together, so a child who is
    /// refused can't leave a new family behind with nobody in it.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateFamilyRequest request, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var result = Family.Create(request.Partner1Id, request.Partner2Id, request.Type);
        if (!result.IsSuccess)
        {
            return Results.ValidationProblem(result.Errors.ToDictionary());
        }

        var missing = new Dictionary<string, string[]>();
        foreach (var (field, id) in new[] { ("partner1Id", request.Partner1Id), ("partner2Id", request.Partner2Id) })
        {
            if (id is not null && !await database.People.AnyAsync(person => person.Id == id, cancellationToken))
            {
                missing[field] = ["There's no person with this ID."];
            }
        }

        var children = request.Children ?? [];
        foreach (var child in children)
        {
            if (!await database.People.AnyAsync(person => person.Id == child.ChildId, cancellationToken))
            {
                missing["childId"] = ["There's no person with this ID."];
            }
        }

        if (missing.Count > 0)
        {
            return Results.ValidationProblem(missing);
        }

        var tree = new FamilyGraph(await database.Families.AsNoTracking().ToListAsync(cancellationToken));
        foreach (var child in children)
        {
            var added = result.Value.AddChild(child.ChildId, child.Partner1Relationship, child.Partner2Relationship, tree);
            if (!added.IsSuccess)
            {
                return Results.ValidationProblem(added.Errors.ToDictionary());
            }
        }

        database.Families.Add(result.Value);
        await database.SaveChangesAsync(cancellationToken);

        return Created(result.Value);
    }

    private static async Task<IResult> GetAsync(Guid id, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var family = await database.Families.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

        return family is null ? Results.NotFound() : Results.Ok(ToResponse(family));
    }

    /// <summary>Removes the family and its child links. The people themselves are untouched.</summary>
    private static async Task<IResult> DeleteAsync(Guid id, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var family = await database.Families.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (family is null)
        {
            return Results.NotFound();
        }

        database.Families.Remove(family);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> AddChildAsync(
        Guid id, AddChildRequest request, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var family = await database.Families.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (family is null)
        {
            return Results.NotFound();
        }

        if (!await database.People.AnyAsync(person => person.Id == request.ChildId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["childId"] = ["There's no person with this ID."] });
        }

        var tree = new FamilyGraph(await database.Families.AsNoTracking().ToListAsync(cancellationToken));
        var result = family.AddChild(request.ChildId, request.Partner1Relationship, request.Partner2Relationship, tree);
        if (!result.IsSuccess)
        {
            return Results.ValidationProblem(result.Errors.ToDictionary());
        }

        await database.SaveChangesAsync(cancellationToken);

        return Created(family);
    }

    private static async Task<IResult> RemoveChildAsync(
        Guid id, Guid childId, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var family = await database.Families.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (family is null || !family.RemoveChild(childId))
        {
            return Results.NotFound();
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ForPersonAsync(Guid id, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        if (!await database.People.AnyAsync(person => person.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var families = await database.Families.AsNoTracking()
            .Where(f => f.Partner1Id == id || f.Partner2Id == id || f.Children.Any(child => child.ChildId == id))
            .ToListAsync(cancellationToken);

        var relatives = families.SelectMany(f => f.Partners.Concat(f.Children.Select(child => child.ChildId))).Distinct().ToList();
        var people = await database.People.AsNoTracking()
            .Where(person => relatives.Contains(person.Id))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PersonFamiliesResponse(
            families.Where(f => f.Children.Any(child => child.ChildId == id)).Select(ToResponse).ToList(),
            families.Where(f => f.Partners.Contains(id)).Select(ToResponse).ToList(),
            people.Select(PeopleEndpoints.ToResponse).ToList()));
    }

    private static IResult Created(Family family) =>
        Results.CreatedAtRoute(nameof(FamiliesEndpoints) + nameof(GetAsync), new { id = family.Id }, ToResponse(family));

    private static FamilyResponse ToResponse(Family family) => new(
        family.Id,
        family.Partner1Id,
        family.Partner2Id,
        family.Type,
        family.Children.OrderBy(child => child.Position)
            .Select(child => new ChildResponse(child.ChildId, child.Partner1Relationship, child.Partner2Relationship))
            .ToList());
}
