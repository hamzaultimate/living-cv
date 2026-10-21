using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Portfolio.Data.Seeding;
using Portfolio.Web.Configuration;
using Xunit;

namespace Portfolio.Web.Tests.Configuration;

public class SeedModeConfigurationTests
{
    private static IConfiguration Config(string? mode) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:Mode"] = mode }).Build();

    [Theory]
    [InlineData("Authoritative")]
    [InlineData("authoritative")]
    [InlineData("AUTHORITATIVE")]
    [InlineData(" Authoritative ")]
    public void Authoritative_IsCaseInsensitive(string value) =>
        Assert.Equal(SeedMode.Authoritative, Config(value).GetSeedMode());

    [Theory]
    [InlineData("IfEmpty")]
    [InlineData("ifempty")]
    public void IfEmpty_IsRecognised(string value) =>
        Assert.Equal(SeedMode.IfEmpty, Config(value).GetSeedMode());

    [Theory]
    [InlineData("2")]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("IfEmpty, Authoritative")]
    [InlineData("authorative")]
    public void MalformedValue_MeansIfEmpty_AndWarns(string value)
    {
        var log = new ListLogger();

        Assert.Equal(SeedMode.IfEmpty, Config(value).GetSeedMode(log));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(value.Trim()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void MissingOrEmpty_MeansIfEmpty_WithoutWarning(string? value)
    {
        var log = new ListLogger();

        Assert.Equal(SeedMode.IfEmpty, Config(value).GetSeedMode(log));
        Assert.Empty(log.Entries);
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
