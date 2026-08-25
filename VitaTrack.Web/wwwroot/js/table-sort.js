(function () {
    document.querySelectorAll('table[data-sortable]').forEach(function (table) {
        const tbody = table.querySelector('tbody');
        if (!tbody) return;
        const headers = table.querySelectorAll('th[data-sort-key]');
        let currentKey = null;
        let asc = true;

        headers.forEach(function (th) {
            th.style.cursor = 'pointer';
            th.addEventListener('click', function () {
                const key = th.dataset.sortKey;
                const type = th.dataset.sortType || 'text';
                if (currentKey === key) { asc = !asc; } else { currentKey = key; asc = true; }
                sortRows(tbody, key, type, asc);
                headers.forEach(function (h) {
                    h.textContent = h.textContent.replace(/ [▲▼]$/, '');
                });
                th.textContent = th.textContent.trim() + (asc ? ' ▲' : ' ▼');
            });
        });

        function sortRows(tbody, key, type, asc) {
            const rows = Array.from(tbody.querySelectorAll('tr'));
            rows.sort(function (a, b) {
                const av = cellValue(a, key);
                const bv = cellValue(b, key);
                let cmp;
                if (type === 'number') { cmp = (parseFloat(av) || 0) - (parseFloat(bv) || 0); }
                else { cmp = String(av).localeCompare(String(bv), undefined, { numeric: true, sensitivity: 'base' }); }
                return asc ? cmp : -cmp;
            });
            rows.forEach(function (r) { tbody.appendChild(r); });
        }

        function cellValue(row, key) {
            const cell = row.querySelector('td[data-sort-key="' + key + '"]');
            if (!cell) return '';
            return cell.dataset.sortValue !== undefined ? cell.dataset.sortValue : cell.textContent.trim();
        }
    });
})();
