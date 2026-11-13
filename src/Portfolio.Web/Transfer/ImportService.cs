using Microsoft.AspNetCore.Http;

namespace Portfolio.Web.Transfer;

/// <summary>What a preview request came to: a held preview, or the reason the file was not read.</summary>
public sealed record PreviewOutcome(ImportPlan? Plan, string? Refusal);

/// <summary>The import in its two steps: preview (reads the file, writes nothing, holds the plan) and confirm (takes the plan back once and writes it).</summary>
public sealed class ImportService
{
    private readonly ImportPlanner _planner;
    private readonly ImportApplier _applier;
    private readonly ImportPlanStore _store;

    public ImportService(ImportPlanner planner, ImportApplier applier, ImportPlanStore store)
    {
        _planner = planner;
        _applier = applier;
        _store = store;
    }

    public async Task<PreviewOutcome> PreviewAsync(IFormFile? file, bool applyProfile, string sessionKey, CancellationToken ct)
    {
        if (file is null) return new PreviewOutcome(null, "Choose a file to import.");
        using var read = await ImportFileReader.ReadAsync(file.Length > 0 ? file.OpenReadStream() : null, file.Length, ct);
        return await PlanAsync(read, FileNameOf(file.FileName), applyProfile, sessionKey, ct);
    }

    /// <summary>Previews bytes already in memory (the same steps as an upload).</summary>
    public async Task<PreviewOutcome> PreviewAsync(byte[] bytes, string fileName, bool applyProfile, string sessionKey, CancellationToken ct)
    {
        if (bytes.Length > TransferLimits.MaxFileBytes) return new PreviewOutcome(null, $"That file is larger than {ImportFileReader.SizeLabel}, so it was not read.");
        using var read = ImportFileReader.Parse(bytes);
        return await PlanAsync(read, fileName, applyProfile, sessionKey, ct);
    }

    private async Task<PreviewOutcome> PlanAsync(ReadResult read, string fileName, bool applyProfile, string sessionKey, CancellationToken ct)
    {
        if (read.Refusal is not null) return new PreviewOutcome(null, read.Refusal);
        var outcome = await _planner.PlanAsync(read.Document!.RootElement, fileName, applyProfile, sessionKey, ct);
        if (outcome.Plan is null) return new PreviewOutcome(null, outcome.Refusal);
        _store.Hold(outcome.Plan);
        return new PreviewOutcome(outcome.Plan, null);
    }

    /// <summary>The held preview for <paramref name="token"/>, taken so it cannot be confirmed twice; null when there is none for this session.</summary>
    public ImportPlan? Take(string? token, string sessionKey) => _store.Take(token, sessionKey);

    public bool Discard(string? token, string sessionKey) => _store.Discard(token, sessionKey);

    public Task<ImportResult> ApplyAsync(ImportPlan plan, CancellationToken ct) => _applier.ApplyAsync(plan, ct);

    /// <summary>A file name from the browser, reduced to its last segment and shown plainly.</summary>
    public static string FileNameOf(string? name)
    {
        var last = (name ?? "").Replace('\\', '/').Split('/').LastOrDefault() ?? "";
        var plain = new string(last.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return plain.Length == 0 ? "the file" : plain.Length <= 80 ? plain : plain[..79] + "…";
    }
}
