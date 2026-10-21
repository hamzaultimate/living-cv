namespace Portfolio.Web.Tests.Support;

/// <summary>Writes small seed files into a throwaway directory.</summary>
public sealed class SeedFiles : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory().FullName;

    public SeedFiles Write(string file, string json)
    {
        File.WriteAllText(System.IO.Path.Combine(Path, file), json);
        return this;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
