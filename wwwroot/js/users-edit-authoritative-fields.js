(function () {
    "use strict";

    function cleanPersistedEditDrafts() {
        try {
            var keys = [];
            for (var index = 0; index < window.sessionStorage.length; index += 1) {
                var key = window.sessionStorage.key(index);
                if (key && key.indexOf("vps:form-persist:v1:") === 0 && key.indexOf("users-wizard-edit") !== -1) {
                    keys.push(key);
                }
            }

            keys.forEach(function (key) {
                window.sessionStorage.removeItem(key);
            });
        } catch (error) {
            // Browser storage may be blocked; the form should still render normally.
        }
    }

    function restoreServerAuthoritativeFields() {
        var form = document.querySelector(".users-editor-form-edit[data-user-edit-authoritative='true']");
        if (!form) {
            return;
        }

        Array.prototype.forEach.call(form.querySelectorAll("[data-server-authoritative-value]"), function (field) {
            var serverValue = field.getAttribute("data-server-authoritative-value") || "";
            var currentValue = (field.value || "").trim();
            var looksLikeWrongBrowserFill = /^\d+$/.test(currentValue) || currentValue === field.getAttribute("data-browser-autofill-block-value");

            if (currentValue !== serverValue && (serverValue || looksLikeWrongBrowserFill)) {
                field.value = serverValue;
                field.dispatchEvent(new Event("input", { bubbles: true }));
                field.dispatchEvent(new Event("change", { bubbles: true }));
            }
        });
    }

    function scheduleRestore() {
        cleanPersistedEditDrafts();
        restoreServerAuthoritativeFields();
        window.setTimeout(restoreServerAuthoritativeFields, 75);
        window.setTimeout(restoreServerAuthoritativeFields, 300);
        window.setTimeout(restoreServerAuthoritativeFields, 900);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", scheduleRestore);
    } else {
        scheduleRestore();
    }

    window.addEventListener("pageshow", scheduleRestore);
})();
