document.addEventListener('htmx:beforeRequest', function (e) {
    var el = e.detail.elt;
    if (!el || el.id !== 'enrich-btn') return;

    var table = document.querySelector('#nutrient-editor-container [data-nutrient-count]');
    var count = table ? parseInt(table.getAttribute('data-nutrient-count'), 10) : 0;
    if (count > 0) {
        var ok = window.confirm('This supplement already has nutrients. Enriching may overwrite them. Continue?');
        if (!ok) e.preventDefault();
    }
});
