using Microsoft.AspNetCore.Mvc.RazorPages;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages;

public class IndexModel : PageModel
{
    private readonly PortfolioContentService _repo;
    private readonly RequestTemplate _template;

    public IndexModel(PortfolioContentService repo, RequestTemplate template)
    {
        _repo = repo;
        _template = template;
    }

    public List<Project> FeaturedProjects { get; private set; } = new();
    public List<Testimonial> Testimonials { get; private set; } = new();

    /// <summary>Loaded only when the template lists credentials in its markup or its layout leads with them.</summary>
    public List<Certification> Certifications { get; private set; } = new();

    /// <summary>The most recent roles, for layouts that lead with practice history.</summary>
    public List<Experience> RecentRoles { get; private set; } = new();

    public List<Skill> Skills { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var tpl = await _template.GetAsync();
        var credentialsLayout = tpl.Archetype == Archetypes.Credentials;

        if (tpl.IsEnabled(SectionRegistry.Projects)) FeaturedProjects = await _repo.GetFeaturedProjectsAsync();
        if (tpl.IsEnabled(SectionRegistry.Testimonials)) Testimonials = await _repo.GetTestimonialsAsync();
        if (tpl.IsEnabled(SectionRegistry.Certifications) && (credentialsLayout || tpl.Definition.Seo.CredentialsJsonLd))
            Certifications = await _repo.GetCertificationsAsync();
        if (credentialsLayout && tpl.IsEnabled(SectionRegistry.Experience))
            RecentRoles = (await _repo.GetExperiencesAsync()).Take(3).ToList();
        if (credentialsLayout && tpl.IsEnabled(SectionRegistry.Skills))
            Skills = await _repo.GetSkillsAsync();
    }
}
