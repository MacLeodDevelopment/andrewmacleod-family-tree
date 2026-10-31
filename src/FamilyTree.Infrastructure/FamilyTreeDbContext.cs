using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure;

/// <summary>
/// The family tree's database, through Entity Framework Core. Tables are added as the
/// domain grows, each with a migration (see README, "The database").
/// </summary>
public sealed class FamilyTreeDbContext(DbContextOptions<FamilyTreeDbContext> options) : DbContext(options);
