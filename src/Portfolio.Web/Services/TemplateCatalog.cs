using System.Text.Json;

namespace Portfolio.Web.Services;

/// <summary>Lists and loads profession presets.</summary>
public interface ITemplateCatalog
{
    IReadOnlyCollection<TemplatePreset> All { get; }

    /// <summary>The preset for <paramref name="key"/>, or the default preset when the key is unknown.</summary>
    TemplatePreset Get(string? key);
}

/// <summary>Loads presets from <c>Templates/&lt;key&gt;/template.json</c> under a base directory.
/// A manifest that cannot be read is skipped with a logged error.</summary>
public class TemplateCatalog : ITemplateCatalog
{
    public const string DefaultKey = "software";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, TemplatePreset> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public TemplateCatalog(string templatesDir, ILogger<TemplateCatalog>? log = null)
    {
        if (Directory.Exists(templatesDir))
        {
            foreach (var file in Directory.EnumerateFiles(templatesDir, "template.json", SearchOption.AllDirectories))
            {
                try
                {
                    var preset = JsonSerializer.Deserialize<TemplatePreset>(File.ReadAllText(file), Json);
                    if (preset is null || string.IsNullOrWhiteSpace(preset.Key))
                        throw new InvalidDataException("Manifest has no key.");
                    if (!_byKey.TryAdd(preset.Key, preset))
                        throw new InvalidDataException($"Duplicate template key '{preset.Key}'.");
                }
                catch (Exception ex)
                {
                    log?.LogError(ex, "Skipping template manifest {file}", file);
                }
            }
        }

        if (!_byKey.ContainsKey(DefaultKey))
            throw new InvalidOperationException($"Default template '{DefaultKey}' not found under {templatesDir}.");
    }

    public IReadOnlyCollection<TemplatePreset> All => _byKey.Values;

    public TemplatePreset Get(string? key) =>
        key is not null && _byKey.TryGetValue(key, out var preset) ? preset : _byKey[DefaultKey];
}
