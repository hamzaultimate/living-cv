using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Transfer;

/// <summary>
/// Writes a confirmed plan. Every write goes through the editors that the admin pages use: rows through <see cref="ContentEditor"/> (the row's stamp is the one the preview
/// read, the list stamp guards the order, a commit moves the caches on) and the profile through <see cref="ProfileEditor.UpdateAsync"/> under the stamp the preview recorded.
/// Nothing is deleted. A row whose stamp has moved since the preview, or a key that a new row would now collide with, is refused alone with the reason and the rest go on;
/// a profile that changed since the preview is refused whole and nothing silently overwrites it. Once confirmed the work runs to its end whatever the request does,
/// so the report it returns is true.
/// </summary>
public sealed class ImportApplier
{
    private readonly PortfolioDbContext _db;
    private readonly ContentEditor _content;
    private readonly ProfileEditor _profile;

    private readonly ILogger<ImportApplier>? _log;

    public ImportApplier(PortfolioDbContext db, ContentEditor content, ProfileEditor profile, ILogger<ImportApplier>? log = null)
    {
        _log = log;
        _db = db;
        _content = content;
        _profile = profile;
    }

    public async Task<ImportResult> ApplyAsync(ImportPlan plan, CancellationToken requestToken)
    {
        _ = requestToken; // see the class summary: a confirmed import is not abandoned half way
        var ct = CancellationToken.None;

        var profile = new ProfileReport { Label = plan.Profile.Label, Requested = plan.Profile.Requested, InFile = plan.Profile.InFile, Status = plan.Profile.Status };
        profile.Reasons.AddRange(plan.Profile.Reasons);
        profile.Changes.AddRange(plan.Profile.Changes);
        profile.Warnings.AddRange(plan.Profile.Warnings);
        profile.InertSections.AddRange(plan.Profile.InertSections);

        var applied = false;
        var sections = new List<SectionReport>();
        var notes = new List<string>();
        var stopped = false;
        try
        {
            if (plan.ProfileChange is { } change)
            {
                var outcome = await _profile.UpdateAsync(change.Apply, change.SeenStamp, ct);
                if (outcome == SaveOutcome.Saved) applied = true;
                else
                {
                    profile.Status = RowStatus.Refused;
                    profile.Changes.Clear();
                    profile.Reasons.Add($"The {plan.Profile.Label.ToLowerInvariant()} was changed after the preview was made, so none of it was applied and nothing was overwritten. Preview the file again to see what would change now.");
                }
            }

            foreach (var table in plan.Tables)
            {
                if (stopped) { sections.Add(NotAttempted(table.Report)); continue; }
                var report = await table.ApplyAsync(this, ct);
                sections.Add(report);
                stopped = report.Stopped;
            }
        }
        catch (Exception ex)
        {
            // Whatever was written stays written (each row is its own commit), so the report says exactly how far it got instead of failing the page or claiming success.
            _log?.LogError(ex, "The import stopped after an unexpected error.");
            stopped = true;
            foreach (var table in plan.Tables.Skip(sections.Count)) sections.Add(NotAttempted(table.Report));
        }
        finally
        {
            // The caches must follow every row that was committed, even when the apply stopped part way.
            if (sections.Any(s => s.Count(RowStatus.Create) + s.Count(RowStatus.Update) > 0 || s.Reordered)) await _profile.AfterContentCommitAsync();
        }

        var result = new ImportResult { Profile = profile, Sections = sections, ProfileApplied = applied };
        if (stopped)
        {
            var written = result.Created + result.Updated + (applied ? 1 : 0);
            result.Notes.Add($"The import stopped after {written} {(written == 1 ? "change" : "changes")} because of an unexpected error. What was written stays; the rest was not attempted. Preview the file again to apply what is left.");
        }
        return result;
    }

    /// <summary>The report of a table the apply never reached: every row that would have been written is marked as not imported, with the reason.</summary>
    private static SectionReport NotAttempted(SectionReport planned)
    {
        var report = new SectionReport { Section = planned.Section, Label = planned.Label, ItemLabel = planned.ItemLabel, Stopped = true };
        foreach (var row in planned.Rows)
            report.Rows.Add(row.Status is RowStatus.Create or RowStatus.Update ? row.With(RowStatus.Refused, "Not attempted: the import stopped before it reached this entry.") : row);
        return report;
    }

    public async Task<SectionReport> ApplyTableAsync<T>(TablePlan<T> plan, CancellationToken ct) where T : class, IStampedContent
    {
        var spec = plan.Spec;
        var report = new SectionReport { Section = plan.Report.Section, Label = plan.Report.Label, ItemLabel = plan.Report.ItemLabel };
        var results = plan.Report.Rows.ToDictionary(r => r.Position);
        report.Notes.AddRange(plan.Report.Notes);
        if (plan.Rows.Count == 0 && plan.FileOrder.Count == 0)
        {
            report.Rows.AddRange(plan.Report.Rows);
            return report;
        }

        var itemLower = plan.Report.ItemLabel.ToLowerInvariant();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in await spec.Stored(_db).ToListAsync(ct)) keys.Add(spec.KeyOf(row));

        // New rows are placed after the positions as they are now. For a list with a manual order the list stamp is taken right before the first new row is committed, and the positions are read
        // after it: an order change committed between that read and the first new row's commit makes that commit a conflict. Only the first new row is guarded: later new rows take the
        // next position from the same read, so an editor create committed between two of them can share a position until the order step renumbers (and if another list change overlaps
        // the order step too, the duplicate can remain; the page then tells the owner to reorder). Guarding every new row would cost one list-stamp change per row. Taking the stamp only
        // at the first commit means nothing is staged when nothing is committed (a refused create), and an update before it is never refused because of a stamp it does not need.
        var guardsOrder = spec.FollowsFileOrder && typeof(IOrderedContent).IsAssignableFrom(typeof(T));
        var guarded = false;
        var next = 0;

        var attempted = new HashSet<int>();
        var inFlight = 0;
        try
        {
        foreach (var planned in plan.Rows)
        {
            inFlight = planned.Position;
            attempted.Add(planned.Position);
            var current = results[planned.Position];
            if (planned.Status == RowStatus.Update)
            {
                var row = await spec.Set(_db).FirstOrDefaultAsync(x => x.Id == planned.RowId, ct);
                if (row is null) { results[planned.Position] = current.With(RowStatus.Refused, $"This {itemLower} was removed after the preview was made, so it was not updated."); continue; }
                if (!string.Equals(row.ConcurrencyStamp ?? "", planned.SeenStamp ?? "", StringComparison.Ordinal))
                {
                    _db.Entry(row).State = EntityState.Detached;
                    results[planned.Position] = current.With(RowStatus.Refused, $"This {itemLower} was changed after the preview was made, so it was not updated and nothing was overwritten.");
                    continue;
                }
                _content.Stamp(row, planned.SeenStamp);
                planned.Apply(row);
                var outcome = await _content.CommitAsync(ct, refreshCaches: false);
                results[planned.Position] = outcome switch
                {
                    SaveOutcome.Saved => current,
                    SaveOutcome.Duplicate => current.With(RowStatus.Refused, $"Another {itemLower} already has that address name, so it was not updated."),
                    _ => current.With(RowStatus.Refused, $"This {itemLower} was changed while the import was running, so it was not updated and nothing was overwritten."),
                };
            }
            else
            {
                if (planned.Key.Length > 0 && keys.Contains(planned.Key))
                {
                    results[planned.Position] = current.With(RowStatus.Refused, $"A {itemLower} with the same {spec.KeyWords(plan.View)} was added after the preview was made, so this one was not added.");
                    continue;
                }
                if (!guarded)
                {
                    if (guardsOrder) await _content.BeginOrderChangeAsync(plan.Report.Section, ct);
                    next = 0;
                    foreach (var existing in await spec.Stored(_db).ToListAsync(ct))
                        if (existing is IOrderedContent o) next = Math.Max(next, o.SortOrder);
                    guarded = true;
                }
                var row = spec.NewRow();
                planned.Apply(row);
                row.ConcurrencyStamp = ContentEditor.NewStamp();
                if (row is IOrderedContent ordered) ordered.SortOrder = ++next;
                spec.Set(_db).Add(row);
                var outcome = await _content.CommitAsync(ct, refreshCaches: false);
                if (outcome == SaveOutcome.Saved) keys.Add(planned.Key);
                else guarded = false; // the failed commit dropped the staged stamp: the next new row takes it again and reads the positions again
                if (outcome != SaveOutcome.Saved) results[planned.Position] = current.With(RowStatus.Refused, outcome == SaveOutcome.Duplicate
                    ? $"Another {itemLower} was added with the same address name while the import was running, so this one was not added."
                    : $"This {itemLower} could not be added because the list changed while the import was running.");
            }
        }

        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "The import stopped in {Section} after an unexpected error.", plan.Report.Section);
            _db.ChangeTracker.Clear();
            report.Stopped = true;
            foreach (var planned in plan.Rows.Where(r => !attempted.Contains(r.Position) || r.Position == inFlight))
                results[planned.Position] = results[planned.Position].With(RowStatus.Refused, planned.Position == inFlight
                    ? "This entry was not written: an unexpected error stopped the import here."
                    : "Not attempted: the import stopped before it reached this entry.");
            report.Rows.AddRange(results.Values.OrderBy(r => r.Position));
            return report;
        }

        // Nothing may stay staged for the next table (a refused create stages nothing now, but the rule is cheap to keep).
        if (_db.ChangeTracker.HasChanges()) _db.ChangeTracker.Clear();

        // The order step runs when a row was created, when a skill changed group (either may share a position with a row already there), and when the file names several rows (their order is the file's).
        // A file of one row that only updates a field leaves the order alone.
        var created = results.Values.Any(r => r.Status == RowStatus.Create);
        var regrouped = plan.Rows.Any(r => r.Status == RowStatus.Update && r.GroupChanged && results[r.Position].Status == RowStatus.Update);
        var several = plan.FileOrder.Count > 1 && results.Values.Any(r => r.Status is RowStatus.Create or RowStatus.Update or RowStatus.Unchanged);
        if (spec.FollowsFileOrder && plan.FileOrder.Count >= 1 && (created || regrouped || several))
        {
            OrderResult ordered;
            try
            {
                ordered = await OrderAsync(plan, ct);
            }
            catch (Exception ex)
            {
                // The rows are committed; only the positions failed. The table's report is still returned, with a note, instead of the rows being reported as not attempted.
                _log?.LogError(ex, "The order step of {Section} failed after its rows were written.", plan.Report.Section);
                _db.ChangeTracker.Clear();
                ordered = OrderResult.Failed;
            }
            report.Notes.Clear();
            if (ordered == OrderResult.Failed) report.Notes.Add($"The order of the {plan.Report.Label.ToLowerInvariant()} was not set because of an unexpected error. The rows themselves were written; reorder them from their list.");
            else if (ordered == OrderResult.Conflict) report.Notes.Add($"The order of the {plan.Report.Label.ToLowerInvariant()} was not set, because the list was changed somewhere else while the import was running. The rows themselves were written; reorder them from their list.");
            else if (ordered == OrderResult.Set) { report.Reordered = true; report.Notes.Add($"The order of the {plan.Report.Label.ToLowerInvariant()} now follows the file; any not in the file come after them."); }
        }

        report.Rows.AddRange(results.Values.OrderBy(r => r.Position));
        return report;
    }

    /// <summary>
    /// Sets the positions of a table from the file's order, within each group (a skill's group; one group for the other tables): the rows the file names come first in the file's
    /// order, the rest follow in the order they had. Only rows whose position changes are written, without a new row stamp (like a move), under the section's list stamp taken
    /// before the positions are read, so an order change made at the same moment makes this a conflict instead of leaving two rows in one position.
    /// </summary>
    private enum OrderResult { Unchanged, Set, Conflict, Failed }

    private async Task<OrderResult> OrderAsync<T>(TablePlan<T> plan, CancellationToken ct) where T : class, IStampedContent
    {
        var spec = plan.Spec;
        _db.ChangeTracker.Clear(); // everything is committed; read the positions as they are now, not as an earlier read of them was
        await _content.BeginOrderChangeAsync(plan.Report.Section, ct);

        var rows = await spec.Set(_db).ToListAsync(ct);
        var byKey = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.OrderBy(r => r.Id)) byKey.TryAdd(spec.KeyOf(row), row);

        foreach (var group in rows.GroupBy(r => spec.GroupOf(r)))
        {
            var inFile = new List<T>();
            foreach (var (key, _) in plan.FileOrder)
                if (byKey.TryGetValue(key, out var row) && spec.GroupOf(row) == group.Key && !inFile.Contains(row)) inFile.Add(row);
            var rest = group.Where(r => !inFile.Contains(r)).Cast<IOrderedContent>().OrderBy(r => r.SortOrder).ThenBy(r => r.Id).Cast<T>();
            var position = 1;
            foreach (var row in inFile.Concat(rest))
            {
                // Only the position changes, so the row's own stamp is left alone: a form open for the row stays valid, and the update still checks the stamp it loaded.
                if (row is IOrderedContent ordered && ordered.SortOrder != position) ordered.SortOrder = position;
                position++;
            }
        }
        // Nothing moved: the list stamp that was taken is not replaced, so another open list page stays valid.
        if (!_db.ChangeTracker.Entries<T>().Any(e => e.State == EntityState.Modified))
        {
            _db.ChangeTracker.Clear();
            return OrderResult.Unchanged;
        }
        return await _content.CommitAsync(ct, refreshCaches: false) == SaveOutcome.Saved ? OrderResult.Set : OrderResult.Conflict;
    }
}
