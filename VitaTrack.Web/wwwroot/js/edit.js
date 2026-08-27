document.addEventListener('DOMContentLoaded', function () {
    var enrichBtn = document.getElementById('enrich-btn');
    if (!enrichBtn) return;

    enrichBtn.addEventListener('click', function (e) {
        var count = parseInt(enrichBtn.getAttribute('data-nutrient-count') || '0', 10);
        if (count > 0) {
            var ok = window.confirm('This supplement already has nutrients. Enriching may overwrite them. Continue?');
            if (!ok) { e.preventDefault(); return; }
        }
        // Confirmed (or no nutrients): show wait spinner for the postback.
        // Defer disabling so the triggering button stays enabled long enough
        // for the browser to commit the submit (disabling it inline cancels it).
        var spinner = document.getElementById('enrich-spinner');
        if (spinner) spinner.hidden = false;
        setTimeout(function () {
            enrichBtn.disabled = true;
            var saveBtn = document.querySelector('button[formaction="/Supplement/EditSave"]');
            if (saveBtn) saveBtn.disabled = true;
        }, 0);
    });
});
