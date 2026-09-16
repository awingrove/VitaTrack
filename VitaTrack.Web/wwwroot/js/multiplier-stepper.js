// Multiplier stepper: custom +/- buttons at each end of the number input.
// The input keeps step="any" so any numeric value can be typed; the buttons
// step by the container's data-step (0.25) and clamp at data-min (0.01).
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.multiplier-stepper').forEach(function (group) {
        const input = group.querySelector('input[type="number"]');
        if (!input) return;

        const step = parseFloat(group.dataset.step) || 0.25;
        const min = parseFloat(group.dataset.min) || 0.01;
        const clamp = value => Math.max(min, value);

        const move = delta => {
            const current = parseFloat(input.value);
            const base = isNaN(current) ? 1 : current;
            input.value = clamp(Math.round((base + delta) * 100) / 100);
            input.dispatchEvent(new Event('input', { bubbles: true }));
        };

        const decrement = group.querySelector('[data-stepper="decrement"]');
        const increment = group.querySelector('[data-stepper="increment"]');
        if (decrement) decrement.addEventListener('click', () => move(-step));
        if (increment) increment.addEventListener('click', () => move(step));
    });
});
