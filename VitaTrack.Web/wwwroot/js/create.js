(function () {
    // Disable any HTMX-triggering submit button while its request is in flight.
    function inFlightButton(e) {
        var el = e.detail.elt;
        if (el && el.tagName === 'BUTTON' && el.type === 'submit') return el;
        return null;
    }

    document.addEventListener('htmx:beforeRequest', function (e) {
        var el = e.detail.elt;
        if (el && el.id === 'enrich-btn') {
            var table = document.querySelector('#nutrient-editor-container [data-nutrient-count]');
            var count = table ? parseInt(table.getAttribute('data-nutrient-count'), 10) : 0;
            if (count > 0) {
                var ok = window.confirm('This supplement already has nutrients. Enriching may overwrite them. Continue?');
                if (!ok) {
                    e.preventDefault();
                    return;
                }
            }
        }
        var btn = inFlightButton(e);
        if (btn) btn.disabled = true;
    });

    document.addEventListener('htmx:afterRequest', function (e) {
        var btn = inFlightButton(e);
        if (btn) btn.disabled = false;
    });
})();
