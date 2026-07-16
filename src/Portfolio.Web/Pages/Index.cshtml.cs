using Microsoft.AspNetCore.Mvc.RazorPages;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages;

public class IndexModel : PageModel
{
    private readonly PortfolioContentService _repo;

    public IndexModel(PortfolioContentService repo)
    {
        _repo = repo;
    }

    public List<Project> FeaturedProjects { get; private set; } = new();
    public List<Testimonial> Testimonials { get; private set; } = new();

    public async Task OnGetAsync()
    {
        FeaturedProjects = await _repo.GetFeaturedProjectsAsync();
        Testimonials = await _repo.GetTestimonialsAsync();
    }
}
