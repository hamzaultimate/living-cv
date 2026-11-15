using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

/// <summary>What the menu view needs: the model and whether it is drawn as the icon rail.</summary>
public sealed record AdminMenuView(AdminMenuModel Menu, bool Collapsed);

/// <summary>Draws the admin menu. All decisions (what is in it, which item is current, whether it is collapsed) are made before this is called.</summary>
public class AdminMenuViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(AdminMenuModel menu, bool collapsed) => View(new AdminMenuView(menu, collapsed));
}
