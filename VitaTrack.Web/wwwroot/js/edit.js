document.addEventListener('DOMContentLoaded', function () {
    var enrichBtn = document.getElementById('enrich-btn');
    if (!enrichBtn) return;

    enrichBtn.addEventListener('click', function (e) {
        var count = parseInt(enrichBtn.getAttribute('data-nutrient-count') || '0', 10);
        if (count > 0) {
            var ok = window.confirm('This supplement already has nutrients. Enriching may overwrite them. Continue?');
            if (!ok) e.preventDefault();
        }
    });
});
