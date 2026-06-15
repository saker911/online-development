(function () {
    function serializeFormState(form) {
        var parts = [];
        Array.from(form.elements).forEach(function (el) {
            if (!el.name) {
                return;
            }

            if (el.type === "file") {
                parts.push(el.name + "=" + ((el.files && el.files.length) ? el.files.length : 0));
                return;
            }

            if (el.type === "checkbox" || el.type === "radio") {
                parts.push(el.name + "=" + (el.checked ? "1" : "0"));
                return;
            }

            parts.push(el.name + "=" + (el.value || ""));
        });
        return parts.join("&");
    }

    function init() {
        var confirmModalElement = document.getElementById("confirmActionModal");
        var confirmModalMessage = document.getElementById("confirmActionModalMessage");
        var confirmModalConfirm = document.getElementById("confirmActionModalConfirm");
        var confirmModal = confirmModalElement && window.bootstrap && window.bootstrap.Modal
            ? window.bootstrap.Modal.getOrCreateInstance(confirmModalElement)
            : null;
        var pendingConfirmForm = null;

        document.querySelectorAll("form[data-confirm-message]").forEach(function (form) {
            try {
                form.dataset._initialState = serializeFormState(form);
            } catch (error) {
                form.dataset._initialState = "";
            }

            form.addEventListener("submit", function (event) {
                if (form.dataset.confirmed === "true") {
                    return;
                }

                if (!form.checkValidity()) {
                    return;
                }

                var current = serializeFormState(form);
                var initial = form.dataset._initialState || "";
                var hasChanges = current !== initial;

                if (!hasChanges) {
                    return;
                }

                if (!confirmModal || !confirmModalMessage || !confirmModalConfirm) {
                    return;
                }

                event.preventDefault();
                pendingConfirmForm = form;
                confirmModalMessage.textContent = form.getAttribute("data-confirm-message") || "هل تريد المتابعة؟";
                confirmModal.show();
            });
        });

        if (confirmModalConfirm && confirmModal) {
            confirmModalConfirm.addEventListener("click", function () {
                if (!pendingConfirmForm) {
                    confirmModal.hide();
                    return;
                }

                pendingConfirmForm.dataset.confirmed = "true";
                pendingConfirmForm.submit();
                pendingConfirmForm.dataset.confirmed = "false";
                pendingConfirmForm = null;
                confirmModal.hide();
            });
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();