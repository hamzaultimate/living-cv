using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Services;

/// <summary>How a content write ended.</summary>
public enum SaveOutcome
{
    Saved,

    /// <summary>The row changed (or was deleted) since the form was opened; nothing was written.</summary>
    Conflict,

    /// <summary>The database refused the row for a unique key (a project address taken by a request that ran at the same time).</summary>
    Duplicate,
}

/// <summary>
/// Writes the content rows from the admin area. Every write carries the row's concurrency stamp from the form, so a stale tab is refused
/// rather than overwriting; after a commit the profile version moves on and the "Generated" output cache is evicted (through
/// <see cref="ProfileEditor"/>), so public pages, the sitemap, /cv.pdf and /og.png follow immediately. Rows are never deleted by anything
/// but an explicit delete, and a delete touches only its own row and its own uploaded files. Writes that change the positions in a list (a move, a new entry,
/// a skill changing group) are also serialised per section by the list stamp (<see cref="ListStamp"/>), so two overlapping ones cannot leave two rows in one position.
/// </summary>
public class ContentEditor
{
    private readonly PortfolioDbContext _db;
    private readonly ProfileEditor _profile;
    private readonly UploadStore _uploads;
    private readonly ILogger<ContentEditor>? _log;

    public ContentEditor(PortfolioDbContext db, ProfileEditor profile, UploadStore uploads, ILogger<ContentEditor>? log = null)
    {
        _log = log;
        _db = db;
        _profile = profile;
        _uploads = uploads;
    }

    public static string NewStamp() => Guid.NewGuid().ToString("N");

    /// <summary>Stamps the row as written now, naming the stamp the editor saw when it loaded the row.</summary>
    public void Stamp<T>(T row, string? seenStamp) where T : class, IOrderedContent
    {
        _db.Entry(row).Property(x => x.ConcurrencyStamp).OriginalValue = string.IsNullOrWhiteSpace(seenStamp) ? null : seenStamp;
        row.ConcurrencyStamp = NewStamp();
    }

    /// <summary>The position after the last row of <paramref name="rows"/>.</summary>
    public static int NextOrder(IEnumerable<int> orders) => orders.DefaultIfEmpty(0).Max() + 1;

    /// <summary>
    /// The position before the first row of <paramref name="rows"/>, so a new row sorts first in the admin list and on the public pages (both order by
    /// <see cref="IOrderedContent.SortOrder"/> ascending). Nothing else is written: the existing rows keep their positions, so their order cannot change.
    /// The first row of an empty list gets 1. Positions below 1 are tidied the next time the list is reordered.
    /// </summary>
    public static int FirstOrder(IEnumerable<int> orders) => orders.DefaultIfEmpty(2).Min() - 1;

    /// <summary>Saves the pending changes and, if they were written, refreshes the caches.</summary>
    public async Task<SaveOutcome> CommitAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return SaveOutcome.Conflict;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return SaveOutcome.Duplicate;
        }
        await _profile.AfterContentCommitAsync();
        return SaveOutcome.Saved;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            // SQL Server 2601 and 2627 are unique index and key violations; SQLite (the tests) says "UNIQUE constraint failed".
            if (e is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627) return true;
            if (e.GetType().Name == "SqliteException" && e.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The stamp shown for a list whose order has never been changed (it has no <see cref="ListStamp"/> row yet).</summary>
    public const string InitialListStamp = "0";

    /// <summary>
    /// The stamp of the list <paramref name="section"/> as it is now. A page that offers reordering reads this before it reads the rows and sends it
    /// back with every move, so a move made from a stale copy of the list is refused. A read, so it never creates the row.
    /// </summary>
    public async Task<string> ListStampAsync(string section, CancellationToken ct) =>
        await _db.ListStamps.AsNoTracking().Where(x => x.Section == section).Select(x => x.Stamp).FirstOrDefaultAsync(ct) ?? InitialListStamp;

    /// <summary>
    /// Stages the replacement of the list stamp of <paramref name="section"/>, naming <paramref name="seenStamp"/> as the one the caller saw. The next
    /// <see cref="CommitAsync"/> writes it together with the order changes, and is refused as a conflict if anything else replaced the stamp since:
    /// the update only matches the stamp that was seen, the same way a row's <see cref="IOrderedContent.ConcurrencyStamp"/> works. Returns false when
    /// no stamp was given. Call it before staging anything else on this context: the first change in a section creates the stamp row with a save of
    /// its own, which must not carry other pending changes.
    /// </summary>
    public async Task<bool> GuardOrderAsync(string section, string? seenStamp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(seenStamp)) return false;
        if (_db.ChangeTracker.HasChanges()) throw new InvalidOperationException("The list stamp must be guarded before other changes are staged.");

        var row = await _db.ListStamps.FirstOrDefaultAsync(x => x.Section == section, ct);
        if (row is null)
        {
            row = new ListStamp { Section = section, Stamp = InitialListStamp };
            _db.ListStamps.Add(row);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Another request created it first; use theirs (its stamp has moved on, so a stale caller is refused below).
                _db.Entry(row).State = EntityState.Detached;
                row = await _db.ListStamps.FirstAsync(x => x.Section == section, ct);
            }
        }
        _db.Entry(row).Property(x => x.Stamp).OriginalValue = seenStamp;
        row.Stamp = NewStamp();
        return true;
    }

    /// <summary>
    /// For a write whose result does not depend on what the editor saw (a new entry goes to the end or the top whatever the list looked like):
    /// guards the stamp as it is right now. Call it before reading the positions the write is computed from, so any other order change committed
    /// after this point makes the commit a conflict instead of leaving two rows in one position.
    /// </summary>
    public async Task BeginOrderChangeAsync(string section, CancellationToken ct) =>
        await GuardOrderAsync(section, await ListStampAsync(section, ct), ct);

    /// <summary>
    /// Moves a row one place up or down within <paramref name="scope"/> (the rows it is ordered against, in their current order) and writes
    /// consecutive positions to the rows whose position changed. A row at the end stays where it is. Rows with equal positions are separated.
    /// <paramref name="listStamp"/> is the stamp of the section's list that the page was loaded with: if the order of the list was changed since,
    /// nothing is written and the outcome is a conflict.
    /// </summary>
    public async Task<SaveOutcome> MoveAsync<T>(string section, List<T> scope, int id, int delta, string? listStamp, CancellationToken ct) where T : class, IOrderedContent
    {
        var ordered = scope.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToList();
        var index = ordered.FindIndex(r => r.Id == id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count) return SaveOutcome.Saved;

        if (!await GuardOrderAsync(section, listStamp, ct)) return SaveOutcome.Conflict;
        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].SortOrder == i + 1) continue;
            // Only the position changes, so the row's own stamp is left alone: a form open for this row stays valid. The stamp it was loaded with
            // is still checked by the update, so a position change over a concurrent edit is refused; the list stamp covers every other order change.
            ordered[i].SortOrder = i + 1;
        }
        return await CommitAsync(ct);
    }

    /// <summary>True when <paramref name="url"/> is an uploaded file that a project or the profile photo still points at (other than <paramref name="except"/>).</summary>
    public async Task<bool> IsUploadReferencedAsync(string url, int? exceptProjectId, CancellationToken ct)
    {
        var profile = await _db.SiteProfiles.AsNoTracking().Select(p => new { p.PhotoUrl, p.PhotoUrlLight }).FirstOrDefaultAsync(ct);
        if (profile is not null && (profile.PhotoUrl == url || profile.PhotoUrlLight == url)) return true;
        var projects = await _db.Projects.AsNoTracking().Where(p => p.Id != exceptProjectId).ToListAsync(ct);
        return projects.Any(p => p.HeroImage == url || p.Screenshots.Any(s => s.Url == url));
    }

    /// <summary>Deletes uploaded files that nothing points at any more. Only generated <c>/uploads/…</c> addresses are touched (the store ignores
    /// everything else, such as the images that ship with the site), and a file another row still uses is kept.</summary>
    public async Task DeleteUploadsAsync(IEnumerable<string?> urls, int? exceptProjectId)
    {
        // The row change is committed, so this runs to completion whatever the request does, and a failure on one file must neither fail
        // the save nor stop the others. A file left behind is harmless.
        foreach (var url in urls.Where(u => !string.IsNullOrEmpty(u)).Distinct())
        {
            try
            {
                if (!await IsUploadReferencedAsync(url!, exceptProjectId, CancellationToken.None)) _uploads.TryDelete(url);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Could not clean up the uploaded file {File}.", System.IO.Path.GetFileName(url));
            }
        }
    }

    /// <summary>Removes a file that was stored for a save that did not happen.</summary>
    public void DiscardUpload(string? url) => _uploads.TryDelete(url);
}
