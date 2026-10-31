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

    public DbSet<Family> Families => Set<Family>();

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

            person.OwnsMany(p => p.Events, lifeEvent =>
            {
                lifeEvent.ToTable("PersonEvents");
                lifeEvent.WithOwner().HasForeignKey("PersonId");
                lifeEvent.HasKey(e => e.Id);
                lifeEvent.Property(e => e.Id).ValueGeneratedNever();
                lifeEvent.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
                lifeEvent.Property(e => e.Place).HasMaxLength(LifeEvent.MaxPlaceLength);

                // Stored as entered: the text is the record, and its meaning comes from parsing it.
                lifeEvent.Property(e => e.Date)
                    .HasMaxLength(GenealogicalDate.MaxLength)
                    .HasConversion(date => date!.OriginalText, text => GenealogicalDate.Parse(text).Value);
            });
            person.Navigation(p => p.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
            person.Ignore(p => p.Timeline);
        });

        modelBuilder.Entity<Family>(family =>
        {
            family.ToTable("Families");
            family.HasKey(f => f.Id);
            family.Property(f => f.Id).ValueGeneratedNever();
            family.Property(f => f.Type).HasConversion<string>().HasMaxLength(20);
            family.Ignore(f => f.Partners);

            // Restrict: someone can't be deleted while a family refers to them.
            family.HasOne<Person>().WithMany().HasForeignKey(f => f.Partner1Id).OnDelete(DeleteBehavior.Restrict);
            family.HasOne<Person>().WithMany().HasForeignKey(f => f.Partner2Id).OnDelete(DeleteBehavior.Restrict);

            family.OwnsMany(f => f.Children, child =>
            {
                child.ToTable("FamilyChildren");
                child.WithOwner().HasForeignKey("FamilyId");
                child.HasKey("FamilyId", nameof(ChildLink.ChildId));
                child.Property(c => c.ChildId).ValueGeneratedNever();
                child.Property(c => c.Partner1Relationship).HasConversion<string>().HasMaxLength(20);
                child.Property(c => c.Partner2Relationship).HasConversion<string>().HasMaxLength(20);
                child.HasOne<Person>().WithMany().HasForeignKey(c => c.ChildId).OnDelete(DeleteBehavior.Restrict);
                child.HasIndex(c => c.ChildId);
            });
            family.Navigation(f => f.Children).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
