(function () {
    // Delegated from the document rather than bound to each form at load. A per-form
    // loop covers only what was in the DOM when this file ran, so on a page that swaps
    // content in — the Settings page replaces its whole region after every save — the
    // form the user is now looking at is one this script never saw. Submitting it then
    // deleted the row with no confirmation, which is the thing DESIGN.md requires the
    // hook for.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || !form.dataset || !form.dataset.confirmMessage) return;
        if (!confirm(form.dataset.confirmMessage)) {
            e.preventDefault();
        }
    });
})();
