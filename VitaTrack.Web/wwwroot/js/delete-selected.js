(function () {
    const selectAll = document.getElementById('select-all');
    const checkboxes = document.querySelectorAll('.row-checkbox');
    const deleteBtn = document.getElementById('delete-selected-btn');
    const form = document.getElementById('delete-selected-form');

    if (!selectAll || !form) return;

    const entityName = form.dataset.entityName || 'item';

    function updateDeleteBtn() {
        const anyChecked = document.querySelectorAll('.row-checkbox:checked').length > 0;
        deleteBtn.disabled = !anyChecked;
    }

    selectAll.addEventListener('change', function () {
        checkboxes.forEach(cb => cb.checked = selectAll.checked);
        updateDeleteBtn();
    });

    checkboxes.forEach(cb => cb.addEventListener('change', updateDeleteBtn));

    form.addEventListener('submit', function (e) {
        const checked = document.querySelectorAll('.row-checkbox:checked');
        if (checked.length === 0) {
            e.preventDefault();
            return;
        }
        if (!confirm('Delete ' + checked.length + ' selected ' + entityName + '(s)?')) {
            e.preventDefault();
        }
    });
})();
