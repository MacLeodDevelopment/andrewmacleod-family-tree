using Microsoft.EntityFrameworkCore.Design;

namespace FamilyTree.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` build the model. It only compares the code with the
/// previous migration, so it never connects: the connection string is a placeholder.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FamilyTreeDbContext>
{
    public FamilyTreeDbContext CreateDbContext(string[] args) =>
        new(SqlServerOptions.ForFamilyTree("Server=not-used;Database=familytree"));
}
