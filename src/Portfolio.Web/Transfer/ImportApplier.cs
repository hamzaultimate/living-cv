using Portfolio.Data.Entities;

namespace Portfolio.Web.Transfer;

public sealed class ImportApplier
{
    public Task<SectionReport> ApplyTableAsync<T>(TablePlan<T> plan, CancellationToken ct) where T : class, IStampedContent => throw new NotSupportedException();

    public Task<ImportResult> ApplyAsync(ImportPlan plan, CancellationToken ct) => throw new NotSupportedException();
}
