using System.Text.Json;

namespace Portfolio.Web.Services;

/// <summary>Lists and loads profession templates.</summary>
public interface ITemplateCatalog
{
    /// <summary>Every valid template: the default first, then the rest by name.</summary>
    IReadOnlyCollection<TemplateDefinition> All { get; }

    /// <summary>The template for <paramref name="key"/>, or the default template when the key is unknown.</summary>
    TemplateDefinition Get(string? key);

    /// <summary>The allow-list check for untrusted keys: true only for a template that loaded.</summary>
    bool TryGet(string? key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TemplateDefinition? template);

    /// <summary>The template's sample content, or null when it has none.</summary>
    TemplateStarter? GetStarter(string key);
}

/// <summary>Loads templates from <c>Templates/&lt;key&gt;/template.json</c> under a base directory.
/// Only direct subfolders are scanned, and folders starting with "_" are ignored. A manifest that cannot be read,
/// has an unknown property, fails <see cref="TemplateValidator"/>, or names a starter file that is missing or invalid
/// is skipped with a logged error, never a crash.</summary>
public class TemplateCatalog : ITemplateCatalog
{
    public const string DefaultKey = "software";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private readonly Dictionary<string, TemplateDefinition> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TemplateStarter> _starters = new(StringComparer.OrdinalIgnoreCase);

    public TemplateCatalog(string templatesDir, ILogger<TemplateCatalog>? log = null)
    {
        if (Directory.Exists(templatesDir))
        {
            foreach (var folder in Directory.EnumerateDirectories(templatesDir))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('_')) continue;

                var file = Path.Combine(folder, "template.json");
                if (!File.Exists(file)) continue;
                try
                {
                    var template = JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(file), Json)
                        ?? throw new InvalidDataException("Manifest is empty.");
                    var errors = TemplateValidator.Validate(template, name);

                    TemplateStarter? starter = null;
                    if (errors.Count == 0 && template.Starter is not null)
                    {
                        var starterFile = Path.Combine(folder, template.Starter.File);
                        if (!File.Exists(starterFile)) errors.Add($"starter file '{template.Starter.File}' not found.");
                        else
                        {
                            starter = TemplateStarter.Parse(File.ReadAllText(starterFile));
                            errors.AddRange(TemplateValidator.ValidateStarter(starter));
                        }
                    }

                    if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
                    if (!_byKey.TryAdd(template.Key, template))
                        throw new InvalidDataException($"Duplicate template key '{template.Key}'.");
                    if (starter is not null) _starters[template.Key] = starter;
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

    public IReadOnlyCollection<TemplateDefinition> All =>
        _byKey.Values.OrderBy(t => string.Equals(t.Key, DefaultKey, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public TemplateDefinition Get(string? key) => TryGet(key, out var template) ? template : _byKey[DefaultKey];

    public bool TryGet(string? key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TemplateDefinition? template)
    {
        template = null;
        return !string.IsNullOrWhiteSpace(key) && _byKey.TryGetValue(key.Trim(), out template);
    }

    public TemplateStarter? GetStarter(string key) => _starters.GetValueOrDefault(key);
}
