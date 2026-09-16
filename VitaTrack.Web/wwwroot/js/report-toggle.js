// Report detail-row chevrons: swap the collapsed (right) and expanded (down)
// chevron SVGs inside a collapse toggle when the target expands or hides.
// Bootstrap fires 'show.bs.collapse'/'hide.bs.collapse' on the target element;
// both bubble to document, so one listener covers every toggle on the page.
document.addEventListener('show.bs.collapse', function (event) {
    setChevrons(event.target, true);
});
document.addEventListener('hide.bs.collapse', function (event) {
    setChevrons(event.target, false);
});

function setChevrons(target, expanded) {
    const button = document.querySelector(
        `button[data-bs-toggle="collapse"][data-bs-target="#${target.id}"]`);
    if (!button) return;
    button.querySelectorAll('[data-chevron]').forEach(function (icon) {
        icon.hidden = expanded ? icon.dataset.chevron !== 'expanded' : icon.dataset.chevron !== 'collapsed';
    });
}
