document.addEventListener('DOMContentLoaded', function () {
    var enrichBtn = document.getElementById('enrich-btn');
    if (!enrichBtn) return;

    enrichBtn.addEventListener('click', function (e) {
        var count = parseInt(enrichBtn.getAttribute('data-nutrient-count') || '0', 10);
        if (count > 0) {
            var ok = window.confirm('This supplement already has nutrients. Enriching may overwrite them. Continue?');
            if (!ok) { e.preventDefault(); return; }
        }
        // Confirmed (or no nutrients): show wait spinner and lock the form for the postback
        var spinner = document.getElementById('enrich-spinner');
        if (spinner) spinner.style.display = 'block';
        enrichBtn.disabled = true;
        var saveBtn = document.querySelector('button[formaction="/Supplement/EditSave"]');
        if (saveBtn) saveBtn.disabled = true;
    });
});
