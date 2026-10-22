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
    public DbSet<SiteProfile> SiteProfiles => Set<SiteProfile>();
    public DbSet<OwnerAccount> OwnerAccounts => Set<OwnerAccount>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SiteProfile>(e =>
        {
            // Singleton: the only legal row has Id = 1.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.ToTable(t => t.HasCheckConstraint("CK_SiteProfiles_Singleton", "[Id] = 1"));
            e.Property(x => x.OwnerName).HasMaxLength(SiteProfile.MaxLength.OwnerName);
            e.Property(x => x.BrandName).HasMaxLength(SiteProfile.MaxLength.BrandName);
            e.Property(x => x.Role).HasMaxLength(SiteProfile.MaxLength.Role);
            e.Property(x => x.JobTitle).HasMaxLength(SiteProfile.MaxLength.JobTitle);
            e.Property(x => x.Url).HasMaxLength(SiteProfile.MaxLength.Url);
            e.Property(x => x.GoogleAnalyticsId).HasMaxLength(SiteProfile.MaxLength.GoogleAnalyticsId);
            e.Property(x => x.PhotoUrl).HasMaxLength(SiteProfile.MaxLength.PhotoUrl);
            e.Property(x => x.PhotoUrlLight).HasMaxLength(SiteProfile.MaxLength.PhotoUrlLight);
            e.Property(x => x.TemplateKey).HasMaxLength(SiteProfile.MaxLength.TemplateKey);
            e.Property(x => x.ThemeKey).HasMaxLength(SiteProfile.MaxLength.ThemeKey);
            e.Property(x => x.AccentColor).HasMaxLength(SiteProfile.MaxLength.AccentColor);
            e.Property(x => x.Location).HasMaxLength(SiteProfile.MaxLength.Location);
            e.Property(x => x.OnboardingCompletedAt).IsConcurrencyToken();
            e.OwnsOne(x => x.Hero, o => o.ToJson());
            e.OwnsMany(x => x.Metrics, o => o.ToJson());
            e.OwnsOne(x => x.About, o =>
            {
                o.ToJson();
                o.OwnsMany(a => a.ValueProps);
            });
            e.OwnsMany(x => x.Sections, o => o.ToJson());
        });

        b.Entity<OwnerAccount>(e =>
        {
            // Single account: the primary key plus this check leave room for exactly one row (Id = 1),
            // so a concurrent second insert fails in the database rather than relying on app code.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.ToTable(t => t.HasCheckConstraint("CK_OwnerAccounts_Singleton", "[Id] = 1"));
            e.Property(x => x.UserName).HasMaxLength(OwnerAccount.MaxLength.UserName);
            e.Property(x => x.PasswordHash).HasMaxLength(OwnerAccount.MaxLength.PasswordHash);
            e.Property(x => x.SecurityStamp).HasMaxLength(OwnerAccount.MaxLength.SecurityStamp);
        });

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
