using Microsoft.AspNetCore.Mvc.RazorPages;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages;

public class IndexModel : PageModel
{
    private readonly PortfolioContentService _repo;
    private readonly IWebHostEnvironment _env;

    public IndexModel(PortfolioContentService repo, IWebHostEnvironment env)
    {
        _repo = repo;
        _env = env;
    }

    public List<Project> FeaturedProjects { get; private set; } = new();
    public List<Testimonial> Testimonials { get; private set; } = new();

    /// <summary>True when wwwroot/img/profile.jpg has been provided.</summary>
    public bool HasPhoto => System.IO.File.Exists(Path.Combine(_env.WebRootPath, "img", "profile.jpg"));

    public async Task OnGetAsync()
    {
        FeaturedProjects = await _repo.GetFeaturedProjectsAsync();
        Testimonials = await _repo.GetTestimonialsAsync();
    }
}
