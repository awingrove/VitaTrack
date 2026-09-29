// Compare Selected: rebuilds the Compare href from the checked row checkboxes
// in DOM order, gates it at >= 2 selections, and caps a selection at 5 (the cap
// is client-side only; the server accepts any >= 2).
// Select-all note: delete-selected.js registers its listener first (its script
// tag precedes this one) and flips cb.checked without dispatching 'change', so
// by the time this file's select-all listener runs the checkbox states are
// already final — no dispatchEvent workaround is needed here.
(function () {
    const selectAll = document.getElementById('select-all');
    const compareBtn = document.getElementById('compare-selected-btn');
    const hint = document.getElementById('compare-hint');
    if (!selectAll || !compareBtn || !hint) return;

    function update() {
        let checked = Array.from(document.querySelectorAll('.row-checkbox:checked'));
        if (checked.length > 5) {
            checked.slice(5).forEach(cb => { cb.checked = false; });
            checked = checked.slice(0, 5);
        }
        hint.hidden = checked.length < 5;
        if (checked.length >= 2) {
            compareBtn.href = '/Supplement/Compare?ids=' + checked.map(cb => cb.value).join(',');
            compareBtn.classList.remove('disabled');
            compareBtn.removeAttribute('aria-disabled');
        } else {
            compareBtn.removeAttribute('href');
            compareBtn.classList.add('disabled');
            compareBtn.setAttribute('aria-disabled', 'true');
        }
    }

    document.querySelectorAll('.row-checkbox').forEach(cb => cb.addEventListener('change', update));
    selectAll.addEventListener('change', update);
    update();
})();
