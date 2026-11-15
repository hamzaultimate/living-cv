namespace Portfolio.Web.Services;

/// <summary>
/// Gathers what the menu needs (the template in effect and the entry counts) and builds it. The layout now does this on every admin page, including
/// ones that never read the template before, so a failure of either read must not fail the page: it logs and draws the menu in neutral words
/// and without counts.
/// </summary>
public sealed class AdminChrome
{
    private readonly AdminMenu _menu;
    private readonly RequestTemplate _template;
    private readonly AdminContentCounts _counts;
    private readonly ILogger<AdminChrome> _logger;

    public AdminChrome(AdminMenu menu, RequestTemplate template, AdminContentCounts counts, ILogger<AdminChrome> logger)
    {
        _menu = menu;
        _template = template;
        _counts = counts;
        _logger = logger;
    }

    public async Task<AdminMenuModel> MenuAsync(string path, CancellationToken ct)
    {
        TemplateView? view = null;
        try
        {
            view = await _template.GetAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the template for the admin menu; using neutral wording.");
        }
        var counts = await _counts.GetAsync(ct);
        return _menu.Build(view, path, counts?.Entries);
    }
}
