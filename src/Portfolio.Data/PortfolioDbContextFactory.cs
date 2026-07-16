using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Portfolio.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works without a live database
/// or runtime configuration. The connection string here is only used to select the
/// SQL Server provider for scaffolding — no connection is made.
/// </summary>
public class PortfolioDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    public PortfolioDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseSqlServer("Server=.;Database=portfolio-design;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new PortfolioDbContext(options);
    }
}
