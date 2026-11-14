using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Transfer;

/// <summary>
/// What is particular to one content table when it is imported: the key that decides whether a file row is the same as a stored one, how a row is read and
/// checked (the same rules as its editor), and how a stored row is compared with what the file would make of it. The rest (matching, duplicates,
/// previews, writing, order) is shared and lives in <see cref="ImportPlanner"/> and <see cref="ImportApplier"/>.
/// </summary>
public abstract class TableSpec<T> where T : class, IStampedContent
{
    public abstract string Section { get; }

    /// <summary>The property names of a row that something reads. Others are ignored and counted.</summary>
    public abstract IReadOnlyCollection<string> Known { get; }

    /// <summary>The key of a file row (compared without regard to case), or null when the file row does not give enough to have one.</summary>
    public abstract string? KeyOf(JsonElement row);

    public abstract string KeyOf(T stored);

    /// <summary>A short name for a file row in the preview, or null (the preview then numbers it).</summary>
    public abstract string? NameOf(JsonElement row);

    /// <summary>What makes two rows the same, for a message: "name", "company and title".</summary>
    public abstract string KeyWords(Services.EditorView view);

    public abstract T NewRow();
    public abstract IQueryable<T> Stored(PortfolioDbContext db);
    public abstract DbSet<T> Set(PortfolioDbContext db);

    /// <summary>Reads and checks the row, recording errors on <paramref name="r"/>, and fills <paramref name="patch"/> with what it changes. <paramref name="stored"/> is the matched row, or null for a new one.</summary>
    public abstract void Fill(RowReader r, T? stored, Patch<T> patch);

    /// <summary>The row as the export would write it, to tell whether a change changes anything. The position is not part of it.</summary>
    public abstract string Fingerprint(T row);

    /// <summary>The group a row is ordered within (a skill's group), or null when the whole table is one list.</summary>
    public virtual string? GroupOf(T row) => null;

    /// <summary>False for a table whose public order comes from the rows' dates, so the file's order means nothing.</summary>
    public virtual bool FollowsFileOrder => true;

    protected static string Print<V>(V value) => JsonSerializer.Serialize(value, ExportJson.Options);

    /// <summary>The trimmed text of a file row's property, or null. Used only to build keys and names; the full check comes later.</summary>
    protected static string? Peek(JsonElement row, string name) => RowReader.Peek(row, name);

    protected static string Join(params string?[] parts) => string.Join('\u001F', parts.Select(p => p ?? ""));
}
