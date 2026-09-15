document.addEventListener('DOMContentLoaded', function () {
    const select = document.getElementById('SupplementId');
    const serving = document.getElementById('ServingSize');
    if (!select || !serving) return;

    const update = () => {
        const option = select.options[select.selectedIndex];
        serving.textContent = option && option.dataset.serving ? option.dataset.serving : '—';
    };

    select.addEventListener('change', update);
    update();
});
