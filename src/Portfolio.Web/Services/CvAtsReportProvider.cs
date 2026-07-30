using System.Text.Json;

namespace Portfolio.Web.Services;

public class CvAtsReportProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _reportPath;
    private CvAtsReport? _cached;
    private bool _loaded;

    public CvAtsReportProvider(string seedDirectory)
    {
        _reportPath = Path.Combine(seedDirectory, "ats-report.json");
    }

    public CvAtsReport? GetReport()
    {
        if (_loaded) return _cached;

        try
        {
            _cached = File.Exists(_reportPath)
                ? JsonSerializer.Deserialize<CvAtsReport>(File.ReadAllText(_reportPath), JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            _cached = null;
        }

        _loaded = true;
        return _cached;
    }
}
