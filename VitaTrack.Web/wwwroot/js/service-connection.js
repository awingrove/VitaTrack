// Rapid-click protection for the two HTMX forms on the Settings page, per DESIGN.md's
// "HTMX forms must disable their submit button while a request is in flight and re-enable
// on response". Listeners on htmx's own events rather than inline handlers, because CSP
// blocks the latter.
//
// Re-attached after every swap, because the swapped-in fragment carries its own <form>
// and a listener bound to the old one would never fire again — which is the failure
// this file exists to prevent, and the one that made the second submit on the page a
// silent no-op when the guard was bound once at load.
(function () {
    function guard(root) {
        if (!root) return;
        root.querySelectorAll('form[hx-post]').forEach(function (form) {
            if (form.dataset.guarded === 'true') return;
            form.dataset.guarded = 'true';
            form.addEventListener('htmx:beforeRequest', function () {
                setSubmitEnabled(form, false);
            });
            form.addEventListener('htmx:afterRequest', function () {
                setSubmitEnabled(form, true);
            });
        });
    }

    function setSubmitEnabled(form, enabled) {
        form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(function (button) {
            button.disabled = !enabled;
        });
    }

    document.addEventListener('DOMContentLoaded', function () { guard(document); });
    // htmx re-executes an external <script src> found in a swapped fragment, so this
    // document-level listener is registered once and the guard is re-run on each swap.
    document.body.addEventListener('htmx:afterSwap', function (event) { guard(event.target); });
})();
