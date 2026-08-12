(function () {
    const table = document.getElementById('nutrients-table');
    if (!table) return;

    let nutrientIndex = parseInt(table.dataset.nutrientCount || '0', 10);

    document.getElementById('add-nutrient-row').addEventListener('click', function () {
        const tbody = table.querySelector('tbody');
        const emptyRow = tbody.querySelector('.empty-row');
        if (emptyRow) emptyRow.remove();

        const tr = document.createElement('tr');
        tr.innerHTML =
            '<td><input name="nutrients[' + nutrientIndex + '].GenericName" class="form-control" /></td>' +
            '<td><input name="nutrients[' + nutrientIndex + '].SpecificForm" class="form-control" /></td>' +
            '<td><input name="nutrients[' + nutrientIndex + '].Dosage" class="form-control" /></td>' +
            '<td><button type="button" class="btn btn-sm btn-danger remove-row">Remove</button></td>';
        tbody.appendChild(tr);
        nutrientIndex++;

        tr.querySelector('.remove-row').addEventListener('click', function () {
            tr.remove();
            reindexNutrients();
        });
    });

    document.querySelectorAll('.remove-row').forEach(function (btn) {
        btn.addEventListener('click', function () {
            btn.closest('tr').remove();
            reindexNutrients();
        });
    });

    function reindexNutrients() {
        var rows = table.querySelectorAll('tbody tr:not(.empty-row)');
        rows.forEach(function (row, index) {
            row.querySelectorAll('input').forEach(function (input) {
                var name = input.getAttribute('name');
                if (name && name.startsWith('nutrients[')) {
                    var field = name.split('.')[1];
                    input.setAttribute('name', 'nutrients[' + index + '].' + field);
                }
            });
        });

        table.dataset.nutrientCount = rows.length;

        if (rows.length === 0) {
            var tbody = table.querySelector('tbody');
            var emptyRow = document.createElement('tr');
            emptyRow.className = 'empty-row';
            emptyRow.innerHTML = '<td colspan="4" class="text-muted text-center">No nutrients. Click "Add Nutrient" to start.</td>';
            tbody.appendChild(emptyRow);
        }
    }
})();