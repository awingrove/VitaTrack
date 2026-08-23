(function () {
    var parent = document.getElementById('ParentNutrientId');
    var dosage = document.getElementById('Dosage');
    if (!parent || !dosage) return;

    function sync() {
        if (parent.value) {
            dosage.removeAttribute('required');
        } else {
            dosage.setAttribute('required', 'required');
        }
    }

    parent.addEventListener('change', sync);
    sync();
})();
