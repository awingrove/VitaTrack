// wwwroot/js/import-csv.js
(function () {
    document.addEventListener('DOMContentLoaded', function () {
        var form = document.querySelector('#importCsvModal form');
        if (form) {
            form.addEventListener('submit', function () {
                var btn = form.querySelector('button[type=submit]');
                if (btn) btn.disabled = true;

                var spinner = document.getElementById('import-spinner');
                if (spinner) spinner.style.display = 'block';
            });
        }

        var modal = document.getElementById('importCsvModal');
        if (modal) {
            modal.addEventListener('hidden.bs.modal', function () {
                window.location.reload();
            });
        }
    });

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
})();
