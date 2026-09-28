using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Web.Models;

namespace VitaTrack.Web.Controllers;

/// <summary>
/// Settings: where a user points VitaTrack at an AI service. The page, the connect form,
/// and the disconnect action. The two htmx POST targets live in
/// <see cref="ServiceConnectionController.Probe"/>; the split is by request, not by type,
/// and the complete-type size limit is what forced it.
/// </summary>
public partial class ServiceConnectionController(
    IServiceConnectionRepository connections,
    SaveConnectionHandler saveConnection,
    ProbeConnectionHandler probeConnection) : Controller
{
    /// <summary>How much of an API key the screen may show.</summary>
    private const int VisibleKeyChars = 4;

    private const string Mask = "••••";

    private readonly IServiceConnectionRepository _connections = connections;
    private readonly SaveConnectionHandler _saveConnection = saveConnection;
    private readonly ProbeConnectionHandler _probeConnection = probeConnection;

    // GET: /ServiceConnection  (nav bar -> Settings)
    public async Task<IActionResult> Index() =>
        View(await BuildViewModelAsync(new ConnectServiceRequest(), HttpContext.RequestAborted));

    /// <summary>
    /// POST: /ServiceConnection/Delete — disconnect, and only the active row. The
    /// repository has no cascade out of this table by design: no foreign key points at
    /// it, so removing a credential must never touch a supplement.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete()
    {
        var active = await _connections.GetActiveAsync(HttpContext.RequestAborted);
        if (active is not null)
            await _connections.DeleteAsync(active.Id, HttpContext.RequestAborted);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Reads the live connection, projects it for display, and prefills the
    /// form from it — so neither the first visit nor a reconnect makes the user retype
    /// what is already saved. What the form carries wins, so a rejected submit
    /// redisplays exactly what was typed. The API key is never carried across, on any
    /// path. <para>
    /// <paramref name="catalog"/> is the probe's answer, and it is null on every path
    /// that did not just probe: a plain page load must not spend a network round trip on
    /// a navigation, so the picker arrives with no catalog and renders free text.
    /// <paramref name="probeNote"/> is the other half of that. It describes the probe
    /// this response is the answer to, so it is set only when a probe just ran and did
    /// not verify. The saved state carries the lasting truth in its badge; this says why
    /// the field below it is free text right now.
    /// </para></summary>
    private async Task<ServiceConnectionViewModel> BuildViewModelAsync(
        ConnectServiceRequest form,
        CancellationToken ct,
        ModelCatalog? catalog = null,
        string? probeNote = null,
        bool justSaved = false)
    {
        var active = await _connections.GetActiveAsync(ct);
        return new ServiceConnectionViewModel(
            Saved: active is null ? null : new SavedConnection(
                active.Service,
                active.BaseUrl,
                active.Model,
                active.Verification,
                MaskApiKey(active.ApiKey)),
            Form: PrefilledForm(form, active),
            ModelPicker: active is null
                ? null
                : new ModelPickerViewModel(
                    catalog?.Models ?? [],
                    active.Model,
                    active.Variant,
                    ServiceDescriptorRegistry.Variants,
                    justSaved),
            ProbeNote: active is null ? null : probeNote);
    }

    /// <summary>What the connect form shows. Each field takes the typed value if there is
    /// one, else what is already saved, so a rejected submit never silently swaps the
    /// user's half-typed URL for the saved one — they could not tell which value the
    /// form was about to save.
    /// <para>
    /// The key is never one of those: it comes back empty on every path, including the
    /// one where the user just typed it. The only thing that kept the raw key out of the
    /// redisplayed HTML was that InputTagHelper drops `value` for type="password" — an
    /// accident of the control type, not a rule of this code. Delete this line before
    /// ever putting the key back.
    /// </para></summary>
    private static ConnectServiceRequest PrefilledForm(ConnectServiceRequest form, ServiceConnection? active) => new()
    {
        Service = string.IsNullOrWhiteSpace(form.Service) ? active?.Service ?? form.Service : form.Service,
        BaseUrl = string.IsNullOrWhiteSpace(form.BaseUrl) ? active?.BaseUrl ?? string.Empty : form.BaseUrl,
        ApiKey = string.Empty,
        Model = string.IsNullOrWhiteSpace(form.Model) ? active?.Model : form.Model,
    };

    /// <summary>Everything but the last few characters. A key no longer than that is
    /// masked whole — showing its last four would be showing all of it.</summary>
    private static string MaskApiKey(string apiKey) =>
        apiKey.Length <= VisibleKeyChars ? Mask : Mask + apiKey[^VisibleKeyChars..];
}
