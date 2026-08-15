// wwwroot/js/import-csv.js
(function () {
    document.body.addEventListener('htmx:afterSwap', function (evt) {
        if (evt.detail.target.id === 'import-report-container') {
            var spinner = document.getElementById('import-spinner');
            if (spinner) spinner.style.display = 'none';
        }
    });
})();
