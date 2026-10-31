using FamilyTree.Domain;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure;

/// <summary>
/// The family tree's database, through Entity Framework Core. Tables are added as the
/// domain grows, each with a migration (see README, "The database").
/// </summary>
public sealed class FamilyTreeDbContext(DbContextOptions<FamilyTreeDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(person =>
        {
            person.ToTable("People");
            person.HasKey(p => p.Id);
            person.Property(p => p.Id).ValueGeneratedNever();
            person.Property(p => p.Sex).HasConversion<string>().HasMaxLength(20);

            person.OwnsMany(p => p.Names, name =>
            {
                name.ToTable("PersonNames");
                name.WithOwner().HasForeignKey("PersonId");
                name.HasKey("PersonId", nameof(PersonName.Position));
                name.Property(n => n.Position).ValueGeneratedNever();
                name.Property(n => n.GivenName).HasMaxLength(PersonName.MaxLength);
                name.Property(n => n.Surname).HasMaxLength(PersonName.MaxLength);
                name.Property(n => n.Type).HasConversion<string>().HasMaxLength(20);
            });
            person.Navigation(p => p.Names).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
