using Microsoft.EntityFrameworkCore;
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

    public ImportApplier(PortfolioDbContext db, ContentEditor content, ProfileEditor profile)
    {
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

        var sections = new List<SectionReport>();
        foreach (var table in plan.Tables) sections.Add(await table.ApplyAsync(this, ct));

        var result = new ImportResult { Profile = profile, Sections = sections, ProfileApplied = applied };
        if (sections.Sum(s => s.Count(RowStatus.Create) + s.Count(RowStatus.Update)) > 0 || sections.Any(s => s.Reordered)) await _profile.AfterContentCommitAsync();
        return result;
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
        var next = 0;
        foreach (var row in await spec.Stored(_db).ToListAsync(ct))
        {
            keys.Add(spec.KeyOf(row));
            if (row is IOrderedContent ordered) next = Math.Max(next, ordered.SortOrder);
        }

        foreach (var planned in plan.Rows)
        {
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
                var row = spec.NewRow();
                planned.Apply(row);
                row.ConcurrencyStamp = ContentEditor.NewStamp();
                if (row is IOrderedContent ordered) ordered.SortOrder = ++next;
                spec.Set(_db).Add(row);
                var outcome = await _content.CommitAsync(ct, refreshCaches: false);
                if (outcome == SaveOutcome.Saved) keys.Add(planned.Key);
                else results[planned.Position] = current.With(RowStatus.Refused, outcome == SaveOutcome.Duplicate
                    ? $"Another {itemLower} was added with the same address name while the import was running, so this one was not added."
                    : $"This {itemLower} could not be added because the list changed while the import was running.");
            }
        }

        if (spec.FollowsFileOrder && plan.FileOrder.Count > 1 && results.Values.Any(r => r.Status is RowStatus.Create or RowStatus.Update or RowStatus.Unchanged))
        {
            var ordered = await OrderAsync(plan, ct);
            report.Notes.Clear();
            if (ordered == OrderResult.Conflict) report.Notes.Add($"The order of the {plan.Report.Label.ToLowerInvariant()} was not set, because the list was changed somewhere else while the import was running. The rows themselves were written; reorder them from their list.");
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
    private enum OrderResult { Unchanged, Set, Conflict }

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
