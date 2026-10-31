namespace FamilyTree.Domain;

public enum PartnershipType
{
    Unknown,
    Marriage,
    CivilPartnership,
    Cohabiting,

    /// <summary>The parents weren't a couple, or there's no recorded partner at all.</summary>
    None,
}

/// <summary>How a child relates to one particular parent.</summary>
public enum ParentRelationship
{
    Biological,
    Adopted,
    Step,
    Foster,
    Presumed,
    Unknown,
}

/// <summary>
/// A child in a family. The relationship is held for each parent, because a child can be
/// biological to their mother and a step-child to her partner.
/// </summary>
public sealed class ChildLink
{
    // For Entity Framework, which sets the properties itself.
    private ChildLink()
    {
    }

    internal ChildLink(Guid childId, int position, ParentRelationship toPartner1, ParentRelationship toPartner2)
    {
        ChildId = childId;
        Position = position;
        Partner1Relationship = toPartner1;
        Partner2Relationship = toPartner2;
    }

    public Guid ChildId { get; private set; }

    /// <summary>The child's place among their siblings in this family.</summary>
    public int Position { get; private set; }

    public ParentRelationship Partner1Relationship { get; private set; }

    public ParentRelationship Partner2Relationship { get; private set; }
}

/// <summary>
/// A parental or partner unit: up to two people, and their children. Children belong to a
/// family, not to a person, so it's always clear which partnership a child came from, even
/// when a parent has children with several partners. A person can be a child in more than
/// one family, such as a birth family and an adoptive family. Half-siblings aren't recorded:
/// they follow from sharing one parent across families.
/// </summary>
public sealed class Family
{
    private readonly List<ChildLink> children = [];

    // For Entity Framework, which sets the properties itself.
    private Family()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Null when this parent is unknown. At least one partner is always known.</summary>
    public Guid? Partner1Id { get; private set; }

    public Guid? Partner2Id { get; private set; }

    public PartnershipType Type { get; private set; }

    public IReadOnlyList<ChildLink> Children => children;

    public IEnumerable<Guid> Partners => new[] { Partner1Id, Partner2Id }.Where(id => id is not null).Select(id => id!.Value);

    public static Result<Family> Create(Guid? partner1Id, Guid? partner2Id, PartnershipType type)
    {
        if (partner1Id is null && partner2Id is null)
        {
            return Failure("partner1Id", "A family needs at least one known partner.");
        }

        if (partner1Id == partner2Id)
        {
            return Failure("partner2Id", "A person can't be their own partner.");
        }

        return Result<Family>.Success(new Family
        {
            Id = Guid.CreateVersion7(),
            Partner1Id = partner1Id,
            Partner2Id = partner2Id,
            Type = type,
        });
    }

    /// <summary>
    /// Adds a child. <paramref name="tree"/> is the tree as it stands, used to refuse anyone
    /// who would become their own ancestor. A parent who is unknown has an unknown relationship.
    /// </summary>
    public Result<ChildLink> AddChild(Guid childId, ParentRelationship toPartner1, ParentRelationship toPartner2, FamilyGraph tree)
    {
        if (children.Any(child => child.ChildId == childId))
        {
            return Result<ChildLink>.Failure(Error("childId", "This child is already in this family."));
        }

        if (Partners.Contains(childId))
        {
            return Result<ChildLink>.Failure(Error("childId", "A person can't be their own parent."));
        }

        var descendants = tree.DescendantsOf(childId);
        if (Partners.Any(descendants.Contains))
        {
            return Result<ChildLink>.Failure(Error("childId", "This would make someone their own ancestor."));
        }

        var link = new ChildLink(
            childId,
            children.Count == 0 ? 0 : children.Max(child => child.Position) + 1,
            Partner1Id is null ? ParentRelationship.Unknown : toPartner1,
            Partner2Id is null ? ParentRelationship.Unknown : toPartner2);
        children.Add(link);

        return Result<ChildLink>.Success(link);
    }

    /// <summary>Takes a child out of this family. The person themselves is untouched.</summary>
    public bool RemoveChild(Guid childId) => children.RemoveAll(child => child.ChildId == childId) > 0;

    private static Result<Family> Failure(string field, string message) => Result<Family>.Failure(Error(field, message));

    private static Dictionary<string, string[]> Error(string field, string message) => new() { [field] = [message] };
}

/// <summary>
/// The family tree as parent-to-child links, for walking through it. A family of thousands
/// fits easily in memory, which keeps this simple code rather than recursive SQL.
/// </summary>
public sealed class FamilyGraph
{
    private readonly ILookup<Guid, Guid> childrenOf;

    public FamilyGraph(IEnumerable<Family> families)
    {
        childrenOf = families
            .SelectMany(family => family.Partners.SelectMany(parent => family.Children.Select(child => (parent, child.ChildId))))
            .ToLookup(link => link.parent, link => link.ChildId);
    }

    /// <summary>Everyone descended from this person, through any kind of parent link.</summary>
    public HashSet<Guid> DescendantsOf(Guid personId)
    {
        var found = new HashSet<Guid>();
        var toVisit = new Stack<Guid>(childrenOf[personId]);
        while (toVisit.TryPop(out var descendant))
        {
            if (found.Add(descendant))
            {
                foreach (var child in childrenOf[descendant])
                {
                    toVisit.Push(child);
                }
            }
        }

        return found;
    }
}
