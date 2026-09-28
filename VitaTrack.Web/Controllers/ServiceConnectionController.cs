using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Web.Models;

namespace VitaTrack.Web.Controllers;

/// <summary>
/// Settings: where a user points VitaTrack at an AI service. A plain form POST and
/// redirect — the model picker that HTMX will serve arrives with it, so there is
/// nothing to swap into here yet.
/// </summary>
public class ServiceConnectionController(
    IServiceConnectionRepository connections,
    SaveConnectionHandler saveConnection) : Controller
{
    /// <summary>How much of an API key the screen may show.</summary>
    private const int VisibleKeyChars = 4;

    private const string Mask = "••••";

    private readonly IServiceConnectionRepository _connections = connections;
    private readonly SaveConnectionHandler _saveConnection = saveConnection;

    // GET: /ServiceConnection  (nav bar -> Settings)
    public async Task<IActionResult> Index()
        => View(await BuildViewModelAsync(new ConnectServiceRequest()));

    // POST: /ServiceConnection/Connect
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(ConnectServiceRequest request)
    {
        if (!ModelState.IsValid)
            return View(nameof(Index), await BuildViewModelAsync(request));

        var result = await _saveConnection.HandleAsync(request);
        if (!result.Succeeded)
        {
            // The write reported no row. Re-render with the error rather than
            // redirecting to a page that would claim the connection was saved.
            ModelState.AddModelError(string.Empty, result.Error!); // null warning is wrong: Succeeded is false, so Error is set
            return View(nameof(Index), await BuildViewModelAsync(request));
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Reads the live connection, projects it for display, and prefills the
    /// form from it — so neither the first visit nor a reconnect makes the user
    /// retype what is already saved. What the form carries wins, so a rejected submit
    /// redisplays exactly what was typed. The API key is deliberately excluded: it is
    /// a password input, and the user re-enters it on every save.</summary>
    private async Task<ServiceConnectionViewModel> BuildViewModelAsync(ConnectServiceRequest form)
    {
        var active = await _connections.GetActiveAsync();
        return new ServiceConnectionViewModel(
            Saved: active is null ? null : new SavedConnection(
                active.Service,
                active.BaseUrl,
                active.Model,
                active.Verification,
                MaskApiKey(active.ApiKey)),
            Form: new ConnectServiceRequest
            {
                BaseUrl = string.IsNullOrWhiteSpace(form.BaseUrl) ? active?.BaseUrl ?? string.Empty : form.BaseUrl,
                ApiKey = form.ApiKey,
                Model = string.IsNullOrWhiteSpace(form.Model) ? active?.Model : form.Model
            });
    }

    /// <summary>Everything but the last few characters. A key no longer than that is
    /// masked whole — showing its last four would be showing all of it.</summary>
    private static string MaskApiKey(string apiKey) =>
        apiKey.Length <= VisibleKeyChars ? Mask : Mask + apiKey[^VisibleKeyChars..];
}
