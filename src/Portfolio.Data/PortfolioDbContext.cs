using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;

namespace Portfolio.Data;

public class PortfolioDbContext : DbContext
{
    public PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Experience> Experiences => Set<Experience>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<Certification> Certifications => Set<Certification>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<Talk> Talks => Set<Talk>();
    public DbSet<Testimonial> Testimonials => Set<Testimonial>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(120);
            e.Property(x => x.HeroImage).HasMaxLength(400);
            e.Property(x => x.Category).HasMaxLength(80);
            // List<string> Impact/Tech map to JSON automatically (EF Core primitive collections).
            e.OwnsMany(x => x.Links, o => o.ToJson());
            e.OwnsMany(x => x.Proofs, o => o.ToJson());
            e.OwnsMany(x => x.Screenshots, o => o.ToJson());
        });

        b.Entity<Experience>(e =>
        {
            e.Property(x => x.Company).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(200);
        });

        b.Entity<Skill>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Group).HasMaxLength(60);
        });

        b.Entity<Certification>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Issuer).HasMaxLength(160);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.OwnsMany(x => x.Proofs, o => o.ToJson());
        });

        b.Entity<BlogPost>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(120);
        });

        b.Entity<Talk>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Testimonial>(e =>
        {
            e.Property(x => x.Author).HasMaxLength(120);
            e.Property(x => x.AuthorTitle).HasMaxLength(200);
            e.Property(x => x.Quote).HasMaxLength(2000);
        });
    }
}
