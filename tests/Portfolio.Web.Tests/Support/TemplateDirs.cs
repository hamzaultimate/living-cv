using System.Text.Json;
using System.Text.Json.Nodes;

namespace Portfolio.Web.Tests.Support;

/// <summary>Throwaway Templates folders for tests: the shipped software template plus one extra manifest derived from another shipped template.</summary>
public static class TemplateDirs
{
    public static string Web
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Portfolio.slnx"))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."), "src", "Portfolio.Web");
        }
    }

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static JsonObject Read(string path) =>
        JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();

    /// <summary>Software plus <paramref name="key"/> (a copy of <paramref name="baseTemplate"/> with its key set and <paramref name="mutate"/> applied).
    /// Without <paramref name="starterFrom"/> the extra template has no starter file; with it, that template's starter.json is copied and optionally edited by <paramref name="starter"/>.
    /// The extra template has no thumbnail.</summary>
    public static string WithExtra(string key, Action<JsonObject> mutate, string baseTemplate = "healthcare", Action<JsonObject>? starter = null, string? starterFrom = null)
    {
        var dir = Directory.CreateTempSubdirectory("lcv-templates-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "software"));
        File.Copy(Path.Combine(Shipped, "software", "template.json"), Path.Combine(dir, "software", "template.json"));
        File.Copy(Path.Combine(Shipped, "software", "preview.png"), Path.Combine(dir, "software", "preview.png"));
        Directory.CreateDirectory(Path.Combine(dir, key));
        var node = Read(Path.Combine(Shipped, baseTemplate, "template.json"));
        node["key"] = key;
        if (starterFrom is null) node.Remove("starter");
        mutate(node);
        File.WriteAllText(Path.Combine(dir, key, "template.json"), node.ToJsonString());
        if (starterFrom is not null)
        {
            var s = Read(Path.Combine(Shipped, starterFrom, "starter.json"));
            starter?.Invoke(s);
            File.WriteAllText(Path.Combine(dir, key, "starter.json"), s.ToJsonString());
        }
        return dir;
    }
}
