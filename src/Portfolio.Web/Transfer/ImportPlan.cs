using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Transfer;

/// <summary>What an import would do with one row, or what it did.</summary>
public enum RowStatus
{
    /// <summary>No row matches: it would be added (after the apply: it was).</summary>
    Create,

    /// <summary>A row with the same key exists and the file changes it.</summary>
    Update,

    /// <summary>A row with the same key exists and already holds what the file says: nothing to write.</summary>
    Unchanged,

    /// <summary>The row is not imported, and the reasons say why. Other rows are not affected.</summary>
    Refused,
}

/// <summary>One row of the file as the preview or the result tells it. Reasons explain a refusal; warnings are things that were changed or dropped on the way in.</summary>
public sealed record RowReport(int Position, string Name, RowStatus Status, IReadOnlyList<string> Reasons, IReadOnlyList<string> Warnings)
{
    public RowReport With(RowStatus status, params string[] reasons) => this with { Status = status, Reasons = [.. Reasons, .. reasons] };
}

/// <summary>The rows of one table: what the file holds for it and what would happen to each row.</summary>
public sealed class SectionReport
{
    public required string Section { get; init; }

    /// <summary>The section's name in the template's words.</summary>
    public required string Label { get; init; }

    /// <summary>What one entry is called in the template's words.</summary>
    public required string ItemLabel { get; init; }

    public List<RowReport> Rows { get; } = [];

    /// <summary>Notes about the table as a whole (too many rows, the order that will be set).</summary>
    public List<string> Notes { get; } = [];

    public int Count(RowStatus status) => Rows.Count(r => r.Status == status);
}

/// <summary>The profile part of the file: whether it was asked for, whether it would apply, and what it would change.</summary>
public sealed class ProfileReport
{
    /// <summary>The profile's name in the template's words.</summary>
    public required string Label { get; init; }

    /// <summary>False when the owner did not tick "also apply the profile": the file's profile is then ignored whatever it holds.</summary>
    public bool Requested { get; init; }

    /// <summary>False when the file has no profile part.</summary>
    public bool InFile { get; init; }

    public RowStatus Status { get; set; } = RowStatus.Unchanged;
    public List<string> Reasons { get; } = [];
    public List<string> Changes { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>Section settings in the file for keys the template does not list: they are not applied and never switch anything on.</summary>
    public List<string> InertSections { get; } = [];
}

/// <summary>A row ready to be written: what it matched, the stamp it had at the preview, and the validated change. Nothing here is the file's raw text.</summary>
public sealed class PlannedRow<T> where T : class
{
    public required int Position { get; init; }
    public required string Name { get; init; }
    public required string Key { get; init; }
    public RowStatus Status { get; set; }

    /// <summary>The database id of the row it matched, for an update.</summary>
    public int? RowId { get; init; }

    /// <summary>The row's stamp when the preview read it: an update is only applied while the row still has it.</summary>
    public string? SeenStamp { get; init; }

    public required IReadOnlyList<Action<T>> Steps { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The group the row is ordered within (a skill's group), or null when the whole table is one list.</summary>
    public string? Group { get; init; }

    public void Apply(T row)
    {
        foreach (var step in Steps) step(row);
    }
}

/// <summary>One table's planned rows and how to write them. Implemented once, generically, by <see cref="TablePlan{T}"/>.</summary>
public interface ITablePlan
{
    SectionReport Report { get; }
    Task<SectionReport> ApplyAsync(ImportApplier applier, CancellationToken ct);
}

public sealed class TablePlan<T> : ITablePlan where T : class, IStampedContent
{
    public required SectionReport Report { get; init; }
    public required TableSpec<T> Spec { get; init; }

    /// <summary>The words of the template the preview was made in, for messages written when the plan is applied.</summary>
    public required EditorView View { get; init; }
    public List<PlannedRow<T>> Rows { get; } = [];

    /// <summary>Every valid row of the file (changed or not) in the file's order, with the group it is ordered in: what sets the order on the site after the rows are written.</summary>
    public List<(string Key, string? Group)> FileOrder { get; } = [];

    public Task<SectionReport> ApplyAsync(ImportApplier applier, CancellationToken ct) => applier.ApplyTableAsync(this, ct);
}

/// <summary>The validated profile change, ready to be written under the stamp the preview recorded.</summary>
public sealed class ProfilePlan
{
    public required string? SeenStamp { get; init; }
    public required Action<SiteProfile> Apply { get; init; }
}

/// <summary>
/// Everything a preview worked out, held on the server until it is confirmed (or expires). The confirm request carries only the token: the owner cannot alter what
/// will be written, and nothing in the plan is the file's raw text.
/// </summary>
public sealed class ImportPlan
{
    public string Token { get; set; } = "";

    /// <summary>The owner session the preview was made in: a token is useless from any other session.</summary>
    public required string SessionKey { get; init; }

    public DateTimeOffset Expires { get; set; }
    public required string FileName { get; init; }
    public bool ApplyProfile { get; init; }

    public required ProfileReport Profile { get; init; }
    public ProfilePlan? ProfileChange { get; set; }

    /// <summary>The template whose words the messages use (the file's, when the profile is applied and its template is installed).</summary>
    public required TemplateView Words { get; init; }

    public List<ITablePlan> Tables { get; } = [];

    /// <summary>Notes about the file as a whole: properties that were ignored, tables that were left out.</summary>
    public List<string> FileNotes { get; } = [];

    public int UnknownProperties { get; set; }

    public IEnumerable<SectionReport> Reports => Tables.Select(t => t.Report);

    public int Count(RowStatus status) => Reports.Sum(r => r.Count(status)) + (Profile.Requested && Profile.InFile && Profile.Status == status ? 1 : 0);

    /// <summary>True when applying would write something.</summary>
    public bool HasChanges => Count(RowStatus.Create) + Count(RowStatus.Update) > 0;
}

/// <summary>What an apply did.</summary>
public sealed class ImportResult
{
    public required ProfileReport Profile { get; init; }
    public required IReadOnlyList<SectionReport> Sections { get; init; }
    public List<string> Notes { get; } = [];
    public int Created => Sections.Sum(s => s.Count(RowStatus.Create));
    public int Updated => Sections.Sum(s => s.Count(RowStatus.Update));
    public int Unchanged => Sections.Sum(s => s.Count(RowStatus.Unchanged));
    public int Refused => Sections.Sum(s => s.Count(RowStatus.Refused));
    public bool ProfileApplied { get; set; }
}
