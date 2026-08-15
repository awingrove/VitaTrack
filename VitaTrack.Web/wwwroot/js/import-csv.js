// wwwroot/js/import-csv.js
(function () {
    document.body.addEventListener('htmx:afterSwap', function (evt) {
        if (evt.detail.target.id === 'import-report-container') {
            var spinner = document.getElementById('import-spinner');
            if (spinner) spinner.style.display = 'none';

            var form = document.querySelector('#importCsvModal form');
            if (form) {
                var btn = form.querySelector('button[type=submit]');
                if (btn) btn.disabled = false;
            }
        }
    });

    document.body.addEventListener('htmx:beforeRequest', function (evt) {
        if (evt.detail.target && evt.detail.target.id === 'import-report-container') {
            var spinner = document.getElementById('import-spinner');
            if (spinner) spinner.style.display = 'block';
        }
    });
})();
