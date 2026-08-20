(function () {
    const table = document.getElementById('nutrients-table');
    if (!table) return;

    let nutrientIndex = parseInt(table.dataset.nutrientCount || '0', 10);
    let rowKeySeq = 0;

    function nextRowKey() { return 'k' + (++rowKeySeq); }

    function appendRowEl(tr) {
        const tbody = table.querySelector('tbody');
        const emptyRow = tbody.querySelector('.empty-row');
        if (emptyRow) emptyRow.remove();
        tbody.appendChild(tr);
    }

    function reindexNutrients() {
        const rows = table.querySelectorAll('tbody tr:not(.empty-row)');
        const keyToIndex = {};
        rows.forEach(function (row, index) { keyToIndex[row.dataset.rowKey] = index; });

        rows.forEach(function (row, index) {
            const firstCell = row.querySelector('td');
            row.querySelectorAll('input').forEach(function (input) {
                const name = input.getAttribute('name');
                if (name && name.indexOf('nutrients[') === 0) {
                    const field = name.split('.')[1];
                    input.setAttribute('name', 'nutrients[' + index + '].' + field);
                }
            });

            const parentKey = row.dataset.parentKey;
            let hidden = row.querySelector('input[name$="ParentNutrientId"]');
            if (parentKey) {
                const parentIndex = keyToIndex[parentKey];
                if (!hidden) {
                    hidden = document.createElement('input');
                    hidden.type = 'hidden';
                    firstCell.appendChild(hidden);
                }
                hidden.setAttribute('name', 'nutrients[' + index + '].ParentNutrientId');
                hidden.value = parentIndex;
            } else if (hidden) {
                hidden.remove();
            }
        });

        table.dataset.nutrientCount = rows.length;

        if (rows.length === 0) {
            const tbody = table.querySelector('tbody');
            const emptyRow = document.createElement('tr');
            emptyRow.className = 'empty-row';
            emptyRow.innerHTML = '<td colspan="4" class="text-muted text-center">No nutrients. Click "Add Nutrient" or "Add Blend" to start.</td>';
            tbody.appendChild(emptyRow);
        }
    }

    function removeRow(row) {
        const key = row.dataset.rowKey;
        if (key) {
            table.querySelectorAll('tbody tr[data-parent-key="' + key + '"]').forEach(function (child) { child.remove(); });
        }
        row.remove();
        reindexNutrients();
    }

    function addNutrientRow() {
        const key = nextRowKey();
        const tr = document.createElement('tr');
        tr.dataset.rowKey = key;
        tr.innerHTML =
            '<td><input name="nutrients[' + nutrientIndex + '].GenericName" class="form-control" /></td>' +
            '<td><input name="nutrients[' + nutrientIndex + '].SpecificForm" class="form-control" /></td>' +
            '<td><input name="nutrients[' + nutrientIndex + '].Dosage" class="form-control" required /></td>' +
            '<td><button type="button" class="btn btn-sm btn-danger remove-row">Remove</button></td>';
        appendRowEl(tr);
        nutrientIndex++;
        reindexNutrients();
    }

    function addBlendRow() {
        const key = nextRowKey();
        const tr = document.createElement('tr');
        tr.dataset.rowKey = key;
        tr.innerHTML =
            '<td><input name="nutrients[' + nutrientIndex + '].GenericName" class="form-control" placeholder="Blend name" /></td>' +
            '<td><input name="nutrients[' + nutrientIndex + '].SpecificForm" class="form-control" /></td>' +
            '<td><input name="nutrients[' + nutrientIndex + '].Dosage" class="form-control" required placeholder="Blend dosage" /></td>' +
            '<td>' +
                '<button type="button" class="btn btn-sm btn-outline-primary add-sub-nutrient" data-parent-key="' + key + '">Add sub-nutrient</button> ' +
                '<button type="button" class="btn btn-sm btn-danger remove-row">Remove</button>' +
            '</td>';
        appendRowEl(tr);
        nutrientIndex++;
        reindexNutrients();
    }

    function addSubNutrient(parentKey) {
        const key = nextRowKey();
        const tr = document.createElement('tr');
        tr.dataset.rowKey = key;
        tr.dataset.parentKey = parentKey;
        tr.className = 'blend-child';
        tr.innerHTML =
            '<td style="padding-left:2rem"><input name="nutrients[' + nutrientIndex + '].GenericName" class="form-control" placeholder="Sub-nutrient" /></td>' +
            '<td><input name="nutrients[' + nutrientIndex + '].SpecificForm" class="form-control" /></td>' +
            '<td><input name="nutrients[' + nutrientIndex + '].Dosage" class="form-control" placeholder="optional" /></td>' +
            '<td><button type="button" class="btn btn-sm btn-danger remove-row">Remove</button></td>';
        appendRowEl(tr);
        nutrientIndex++;
        reindexNutrients();
    }

    table.addEventListener('click', function (e) {
        const removeBtn = e.target.closest('.remove-row');
        if (removeBtn) {
            const row = removeBtn.closest('tr');
            if (row) removeRow(row);
            return;
        }
        const subBtn = e.target.closest('.add-sub-nutrient');
        if (subBtn) {
            addSubNutrient(subBtn.dataset.parentKey);
        }
    });

    const addNutrientBtn = document.getElementById('add-nutrient-row');
    if (addNutrientBtn) addNutrientBtn.addEventListener('click', addNutrientRow);

    const addBlendBtn = document.getElementById('add-blend-row');
    if (addBlendBtn) addBlendBtn.addEventListener('click', addBlendRow);

    const saveBtn = document.querySelector('#nutrient-editor-form button[hx-post]');
    if (saveBtn) saveBtn.addEventListener('click', function () { reindexNutrients(); }, true);
})();
