using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Portfolio.Web.Health;

/// <summary>
/// Fast, retry-free SQL Server readiness probe. Uses a raw connection with a short
/// timeout (bypassing EF's retry policy) so misconfiguration surfaces immediately with a
/// clear message instead of hanging. Reads the connection string live from configuration.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IConfiguration _config;

    public DatabaseHealthCheck(IConfiguration config) => _config = config;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var cs = _config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(cs))
            return HealthCheckResult.Unhealthy("ConnectionStrings:Default is not configured.");

        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(cs) { ConnectTimeout = 6 }; }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("Invalid connection string: " + ex.Message, ex); }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cts.Token);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DB_NAME(), (SELECT COUNT(*) FROM sys.tables)";
            await using var reader = await cmd.ExecuteReaderAsync(cts.Token);

            string db = "?";
            int tables = -1;
            if (await reader.ReadAsync(cts.Token)) { db = reader.GetString(0); tables = reader.GetInt32(1); }

            return HealthCheckResult.Healthy($"Connected to '{db}' as expected; {tables} table(s).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"[server='{builder.DataSource}' db='{builder.InitialCatalog}'] {ex.GetType().Name}: {ex.Message}", ex);
        }
    }
}
