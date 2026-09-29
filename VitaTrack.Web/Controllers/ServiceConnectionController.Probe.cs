using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Web.Models;

namespace VitaTrack.Web.Controllers;

/// <summary>
/// The two htmx POST targets. Each answers the same way: htmx gets the whole dynamic
/// region of the page, and a browser without htmx gets a full render or a redirect.
/// That is progressive enhancement rather than a nicety, and it holds only because
/// <em>both</em> forms are real <c>&lt;form&gt;</c> elements carrying
/// <c>asp-action</c> and <c>method="post"</c> as well as <c>hx-post</c> — a form with
/// only the htmx attribute degrades to a GET of the current URL, which throws the
/// submission away without a word. The submit therefore works with the script blocked
/// or still loading, and the user lands somewhere that says what happened.
/// <para>
/// One region rather than two is forced by the rejected case. A connect that fails
/// validation reports field errors on the form, so answering with a fragment that held
/// only the saved state would leave the message nowhere on the page; and answering an
/// htmx request with a full document leaves htmx with nothing it can swap in, which it
/// drops silently. Both symptoms look identical from the outside — the page just does
/// not change — so the region carries the saved state, the picker and the form.
/// </para>
/// </summary>
public partial class ServiceConnectionController
{
    /// <summary>The header htmx sets on a request it issued, and the only thing that
    /// tells this controller to answer with a fragment rather than a page. Written here
    /// rather than read off a helper so the behaviour is visible at the branch.
    /// <para>
    /// <c>internal</c> rather than <c>private</c> so the controller tests send this same
    /// value: a harness with its own copy of the header name would keep passing if the
    /// name here changed, because every request it marked would then take the non-htmx
    /// branch and still be answered — with a redirect, which each test already rejects.
    /// So the duplication was a hazard, not a convenience, and one declaration is the fix.
    /// </para></summary>
    internal const string HtmxHeader = "HX-Request";

    internal const string HtmxHeaderValue = "true";

    /// <summary>The region both POST targets answer into, and the one <c>Index</c> wraps
    /// in <c>#connection-state</c>. It is rendered whether or not anything is saved — an
    /// empty target for a first connect to land in is the alternative, and it does not
    /// exist.</summary>
    private const string ConnectionStateView = "_ConnectionState";

    /// <summary>Said by the unverified branch. Deliberately not worded as an error: a
    /// provider with no <c>/v1/models</c> is ordinary, and a user who reads "failed"
    /// here goes looking for a broken key that is not broken. The badge beside it carries
    /// the durable state; this explains the free-text field underneath it.
    /// <para>
    /// The view does not interpolate this — <c>_Unverified.cshtml</c> takes it as a model
    /// and ignores its value, so nothing user-typed can reach that markup. It is non-null
    /// exactly when a probe just ran and did not verify, which is the only time a plain
    /// page load must not show the branch.
    /// </para></summary>
    private const string CatalogUnavailableNote = "The service did not list its models.";

    /// <summary>
    /// POST: /ServiceConnection/Connect — save, then probe what was just saved, then
    /// answer with the picker (the service listed its models) or the unverified branch
    /// (it did not). A failed probe is neither of those being an error: the connection
    /// is saved either way, and the branch that comes back says so in words.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(ConnectServiceRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Rendered(await BuildViewModelAsync(request, ct));

        var saved = await _saveConnection.HandleAsync(request, ct);
        if (!saved.Succeeded)
            return await RejectedConnect(request, saved.Error, ct);

        // Straight after the save, so this probes the row the save just wrote. The form
        // is rebuilt empty rather than from the submitted values: what is on screen now
        // is the saved connection, and retyping the key is the one thing the saved
        // state cannot do for the user.
        var probed = await _probeConnection.ProbeAsync(ct);
        if (!probed.Succeeded)
            return await RejectedConnect(new ConnectServiceRequest(), probed.Error, ct);

        return Swapped(await BuildViewModelAsync(
            new ConnectServiceRequest(), ct, probed.Catalog, probeNote: UnverifiedProbeNote(probed.Catalog)));
    }

    /// <summary>
    /// POST: /ServiceConnection/SelectModel — the picker writes the model and the
    /// reasoning effort onto the active row. A rejected model comes back with the errors
    /// where the user is looking; a saved one says so, because a silent re-render is
    /// indistinguishable from a no-op.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectModel(SelectModelRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Rendered(await BuildViewModelAsync(new ConnectServiceRequest(), ct));

        var active = await _connections.GetActiveAsync(ct);
        if (active is null)
        {
            ModelState.AddModelError(string.Empty, ProbeConnectionHandler.NoConnection);
            return Rendered(await BuildViewModelAsync(new ConnectServiceRequest(), ct));
        }

        // The variant is the registry's own spelling of what was posted, so the column
        // holds a value from the list rather than a differently-cased copy of one. The
        // request validated, so the lookup cannot be null — the fallback is the compiler
        // being told to trust a claim this method made two lines above, and the registry
        // test covers the mapping it relies on.
        var variant = ServiceDescriptorRegistry.FindVariant(request.Variant) ?? string.Empty;
        var id = await _connections.SaveAsync(
            active with
            {
                Model = request.Model.Trim(),
                Variant = variant,
            },
            ct);

        if (id == IServiceConnectionRepository.NoRowWritten)
        {
            ModelState.AddModelError(string.Empty, "The model could not be saved. Please try again.");
            return Rendered(await BuildViewModelAsync(new ConnectServiceRequest(), ct));
        }

        return Swapped(await BuildViewModelAsync(new ConnectServiceRequest(), ct, justSaved: true));
    }

    /// <summary>A connect the handler refused. The error goes on the operation, not on a
    /// field, because the form is posted from here and the fault may be any of its fields;
    /// what the user typed comes back with it, so they can fix one field rather than
    /// three.
    /// <para>
    /// The null warning on <c>error</c> is wrong: this is only reached when
    /// <c>Succeeded</c> is false, and every <c>Failed(...)</c> carries a message.
    /// </para></summary>
    private async Task<IActionResult> RejectedConnect(ConnectServiceRequest form, string? error, CancellationToken ct)
    {
        ModelState.AddModelError(string.Empty, error!);
        return Rendered(await BuildViewModelAsync(form, ct));
    }

    private static string? UnverifiedProbeNote(ModelCatalog catalog) =>
        catalog.Verified ? null : CatalogUnavailableNote;

    /// <summary>An outcome the user must read: a rejection, or a variant the registry
    /// does not ship. Rendered, never redirected — a redirect would replace the page with
    /// one that no longer carries the message.</summary>
    private IActionResult Rendered(ServiceConnectionViewModel viewModel) =>
        IsHtmxRequest() ? PartialView(ConnectionStateView, viewModel) : View(nameof(Index), viewModel);

    /// <summary>An outcome that changed something. A fragment for htmx; a redirect for a
    /// browser that did not ask for one, so a reload cannot resubmit the form.</summary>
    private IActionResult Swapped(ServiceConnectionViewModel viewModel) =>
        IsHtmxRequest() ? PartialView(ConnectionStateView, viewModel) : RedirectToAction(nameof(Index));

    private bool IsHtmxRequest() => Request.Headers[HtmxHeader] == HtmxHeaderValue;
}
