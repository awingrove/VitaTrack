using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Web.Models;

namespace VitaTrack.Web.ViewComponents;

/// <summary>
/// The connection's verification state as a badge, readable from any page that wants it.
/// <para>
/// The enrichment pages render it, and that is the point: an unverified connection is
/// perfectly usable, so a failed probe produces no error anywhere and the user would
/// otherwise spend a request on a service nobody ever confirmed. Saying it on the page
/// they are about to enrich from is the difference between "the badge is on the settings
/// page" and "the state is visible".
/// </para>
/// <para>
/// A view component rather than a property on the review model, for two reasons that
/// both come down to ownership. The connection belongs to the service-connection slice
/// and the review page belongs to the supplements one, so threading the state through
/// <c>SupplementController</c>'s view models would make that controller know about a
/// table it does not own; and <c>@inject</c>-ing the repository into a view would put a
/// data read in the view. Reading it here keeps both of those out, and keeps the badge
/// markup in one partial the Settings page renders through the same component — so the
/// two pages cannot disagree about what the state looks like.
/// </para>
/// </summary>
public class ConnectionBadgeViewComponent(IServiceConnectionRepository connections) : ViewComponent
{
    /// <summary>The partial's path, named rather than left to convention. The default
    /// location for a view component is <c>Views/Shared/Components/{Name}/Default.cshtml</c>,
    /// which is where this would land — and that is the <em>allowlisted</em> directory, so
    /// the ownership test would stop seeing the markup that decides what "unverified"
    /// looks like. Naming the path keeps the partial a claimed SC artifact and lets
    /// another slice render this component from its own views without a copy.</summary>
    private const string BadgeView = "~/Views/ServiceConnection/_ConnectionBadge.cshtml";

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var active = await connections.GetActiveAsync(HttpContext.RequestAborted);

        // Nothing connected is not a state this badge reports: the enrichment flow
        // already refuses in words and names Settings as the fix, and a second vaguer
        // statement of the same thing on the same screen is noise.
        if (active is null)
            return Content(string.Empty);

        return View(BadgeView, new ConnectionBadgeViewModel(active.Verification));
    }
}
