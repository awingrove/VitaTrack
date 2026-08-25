(function () {
    document.querySelectorAll('form[data-confirm-message]').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            if (!confirm(form.dataset.confirmMessage)) {
                e.preventDefault();
            }
        });
    });
})();
