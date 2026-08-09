(function () {
    "use strict";

    const form = document.querySelector("[data-visitor-workflow-form]");
    if (!form) return;

    const templates = {
        express: {
            identity: [false, false], host: [true, true], location: [false, false], purpose: [true, true],
            lead: 0, days: 30
        },
        standard: {
            identity: [true, false], host: [true, true], location: [true, true], purpose: [true, true],
            lead: 5, days: 30
        },
        secure: {
            identity: [true, true], host: [true, true], location: [true, true], purpose: [true, true],
            lead: 30, days: 14
        }
    };

    const setField = (key, values) => {
        const show = form.querySelector(`[data-workflow-show="${key}"]`);
        const required = form.querySelector(`[data-workflow-require="${key}"]`);
        if (!show || !required) return;
        show.checked = values[0];
        required.checked = values[0] && values[1];
        required.disabled = !values[0];
    };

    const refresh = () => {
        for (const key of ["identity", "host", "location", "purpose"]) {
            const show = form.querySelector(`[data-workflow-show="${key}"]`);
            const required = form.querySelector(`[data-workflow-require="${key}"]`);
            if (show && required) {
                required.disabled = !show.checked;
                if (!show.checked) required.checked = false;
            }
        }

        const identityVisible = form.querySelector('[data-workflow-show="identity"]')?.checked;
        const detailsVisible = ["host", "location", "purpose"].some(key =>
            form.querySelector(`[data-workflow-show="${key}"]`)?.checked
        );
        const identityStep = form.querySelector('[data-workflow-preview-step="identity"]');
        const detailsStep = form.querySelector('[data-workflow-preview-step="details"]');
        if (identityStep) identityStep.hidden = !identityVisible;
        if (detailsStep) detailsStep.hidden = !detailsVisible;
    };

    form.querySelectorAll("[data-workflow-template]").forEach(input => {
        input.addEventListener("change", () => {
            if (!input.checked) return;
            const template = templates[input.dataset.workflowTemplate];
            if (!template) return;
            for (const key of ["identity", "host", "location", "purpose"]) {
                setField(key, template[key]);
            }
            form.querySelector('[name="MinimumLeadMinutes"]').value = template.lead;
            form.querySelector('[name="MaximumAdvanceDays"]').value = template.days;
            refresh();
        });
    });

    form.querySelectorAll("[data-workflow-show], [data-workflow-require]").forEach(input => {
        input.addEventListener("change", refresh);
    });

    refresh();
})();
