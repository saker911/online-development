(function () {
    "use strict";

    var STORAGE_PREFIX = "vps:form-persist:v1:";
    var PENDING_KEY = "vps:form-persist:pending-submits:v1";
    var MAX_PENDING_AGE_MS = 10 * 60 * 1000;
    var FIELD_SELECTOR = "input, textarea, select";
    var originalSubmit = window.HTMLFormElement && window.HTMLFormElement.prototype.submit;

    function canUseSessionStorage() {
        try {
            var testKey = STORAGE_PREFIX + "test";
            window.sessionStorage.setItem(testKey, "1");
            window.sessionStorage.removeItem(testKey);
            return true;
        } catch (error) {
            return false;
        }
    }

    if (!canUseSessionStorage()) {
        return;
    }

    function normalizePath(value) {
        return (value || "").replace(/\/+$/, "").toLowerCase() || "/";
    }

    function getPageKey() {
        return normalizePath(window.location.pathname);
    }

    function isExplicitlyEnabled(form) {
        return form && form.getAttribute("data-form-persist") === "true";
    }

    function isExplicitlyDisabled(form) {
        if (!form) {
            return true;
        }

        var setting = form.getAttribute("data-form-persist");
        return setting === "false" || form.hasAttribute("data-no-form-persist");
    }

    function isSearchLikeForm(form) {
        if (!form) {
            return false;
        }

        var method = (form.getAttribute("method") || "get").toLowerCase();
        var className = form.className || "";
        var role = form.getAttribute("role") || "";
        var ariaLabel = form.getAttribute("aria-label") || "";

        return method === "get"
            || /\b(search|filter)\b/i.test(className)
            || /search|بحث|تصفية/i.test(role + " " + ariaLabel);
    }

    function shouldPersistForm(form) {
        if (!form || isExplicitlyDisabled(form)) {
            return false;
        }

        if (isExplicitlyEnabled(form)) {
            return true;
        }

        return !isSearchLikeForm(form) && getPersistableFields(form).length > 0;
    }

    function getFormIdentifier(form, index) {
        if (form.id) {
            return form.id;
        }

        if (form.getAttribute("name")) {
            return form.getAttribute("name");
        }

        if (form.getAttribute("data-form-persist-id")) {
            return form.getAttribute("data-form-persist-id");
        }

        var action = form.getAttribute("action") || window.location.pathname;
        var method = form.getAttribute("method") || "get";
        return "auto-" + index + "-" + method.toLowerCase() + "-" + normalizePath(action);
    }

    function getFormKey(form) {
        var forms = Array.prototype.slice.call(document.querySelectorAll("form"));
        var index = Math.max(forms.indexOf(form), 0);
        return STORAGE_PREFIX + getPageKey() + ":" + getFormIdentifier(form, index);
    }

    function isPersistableField(field) {
        if (!field || !field.name || field.disabled) {
            return false;
        }

        var tagName = (field.tagName || "").toLowerCase();
        var type = (field.getAttribute("type") || field.type || "").toLowerCase();
        var name = field.name || "";

        if (tagName !== "input" && tagName !== "textarea" && tagName !== "select") {
            return false;
        }

        if (type === "password" || type === "file" || type === "button" || type === "submit" || type === "reset" || type === "image") {
            return false;
        }

        if (name === "__RequestVerificationToken" || field.hasAttribute("data-form-persist-ignore")) {
            return false;
        }

        return true;
    }

    function getPersistableFields(form) {
        try {
            return Array.prototype.filter.call(form.querySelectorAll(FIELD_SELECTOR), isPersistableField);
        } catch (error) {
            return [];
        }
    }

    function getFieldKey(field, index) {
        var type = (field.getAttribute("type") || field.type || "").toLowerCase();
        var base = field.name || field.id || "field-" + index;

        if (type === "radio") {
            return "radio:" + base;
        }

        if (field.id) {
            return base + "#" + field.id;
        }

        return base + "@" + index;
    }

    function readFieldValue(field) {
        var type = (field.getAttribute("type") || field.type || "").toLowerCase();

        if (type === "checkbox") {
            return { kind: "checkbox", checked: !!field.checked };
        }

        if (type === "radio") {
            return { kind: "radio", value: field.value, checked: !!field.checked };
        }

        if (field.tagName && field.tagName.toLowerCase() === "select" && field.multiple) {
            return {
                kind: "select-multiple",
                value: Array.prototype.map.call(field.selectedOptions, function (option) {
                    return option.value;
                })
            };
        }

        return { kind: "value", value: field.value };
    }

    function writeFieldValue(field, stored) {
        if (!stored || !isPersistableField(field)) {
            return false;
        }

        var changed = false;
        var type = (field.getAttribute("type") || field.type || "").toLowerCase();

        if (stored.kind === "checkbox" && type === "checkbox") {
            changed = field.checked !== !!stored.checked;
            field.checked = !!stored.checked;
            return changed;
        }

        if (stored.kind === "radio" && type === "radio") {
            var nextChecked = field.value === stored.value && !!stored.checked;
            changed = field.checked !== nextChecked;
            field.checked = nextChecked;
            return changed;
        }

        if (stored.kind === "select-multiple" && field.tagName.toLowerCase() === "select" && field.multiple) {
            var selectedValues = Array.isArray(stored.value) ? stored.value : [];
            Array.prototype.forEach.call(field.options, function (option) {
                var nextSelected = selectedValues.indexOf(option.value) !== -1;
                changed = changed || option.selected !== nextSelected;
                option.selected = nextSelected;
            });
            return changed;
        }

        if ("value" in field && stored.kind === "value") {
            var nextValue = stored.value == null ? "" : String(stored.value);
            changed = field.value !== nextValue;
            field.value = nextValue;
            return changed;
        }

        return false;
    }

    function dispatchFieldEvents(field) {
        try {
            field.dispatchEvent(new Event("input", { bubbles: true }));
            field.dispatchEvent(new Event("change", { bubbles: true }));
        } catch (error) {
            // Ignore fields with custom event limitations.
        }
    }

    function serializeForm(form) {
        var fields = getPersistableFields(form);
        var entries = {};

        fields.forEach(function (field, index) {
            try {
                var type = (field.getAttribute("type") || field.type || "").toLowerCase();
                var fieldKey = getFieldKey(field, index);

                if (type === "radio") {
                    if (!Object.prototype.hasOwnProperty.call(entries, fieldKey)) {
                        entries[fieldKey] = { kind: "radio", value: null, checked: false };
                    }

                    if (field.checked) {
                        entries[fieldKey] = { kind: "radio", value: field.value, checked: true };
                    }

                    return;
                }

                entries[fieldKey] = readFieldValue(field);
            } catch (error) {
                // Skip unsupported fields and continue.
            }
        });

        return {
            savedAt: new Date().toISOString(),
            path: getPageKey(),
            values: entries
        };
    }

    function saveForm(form) {
        if (!shouldPersistForm(form)) {
            return;
        }

        try {
            window.sessionStorage.setItem(getFormKey(form), JSON.stringify(serializeForm(form)));
        } catch (error) {
            // sessionStorage may be full or blocked. The page should continue normally.
        }
    }

    function clearFormStorage(form) {
        try {
            window.sessionStorage.removeItem(getFormKey(form));
        } catch (error) {
            // Ignore cleanup failure.
        }
    }

    function restoreForm(form) {
        if (!shouldPersistForm(form)) {
            return;
        }

        var raw = null;
        try {
            raw = window.sessionStorage.getItem(getFormKey(form));
        } catch (error) {
            raw = null;
        }

        if (!raw) {
            return;
        }

        try {
            var stored = JSON.parse(raw);
            var values = stored && stored.values ? stored.values : {};
            var changedFields = [];

            getPersistableFields(form).forEach(function (field, index) {
                var fieldKey = getFieldKey(field, index);
                if (!Object.prototype.hasOwnProperty.call(values, fieldKey)) {
                    return;
                }

                try {
                    if (writeFieldValue(field, values[fieldKey])) {
                        changedFields.push(field);
                    }
                } catch (error) {
                    // Ignore this field and keep restoring the rest.
                }
            });

            changedFields.forEach(dispatchFieldEvents);
        } catch (error) {
            try {
                window.sessionStorage.removeItem(getFormKey(form));
            } catch (removeError) {
                // Ignore cleanup failure.
            }
        }
    }

    function readPendingSubmits() {
        try {
            var raw = window.sessionStorage.getItem(PENDING_KEY);
            var parsed = raw ? JSON.parse(raw) : [];
            return Array.isArray(parsed) ? parsed : [];
        } catch (error) {
            return [];
        }
    }

    function writePendingSubmits(items) {
        try {
            window.sessionStorage.setItem(PENDING_KEY, JSON.stringify(items));
        } catch (error) {
            // Ignore persistence failure.
        }
    }

    function hasSuccessSignal() {
        return !!document.querySelector(".app-notify-success, [data-form-persist-clear='true']");
    }

    function cleanupPendingSubmits() {
        var now = Date.now();
        var pageKey = getPageKey();
        var remaining = [];
        var currentPageHasSuccess = hasSuccessSignal();

        readPendingSubmits().forEach(function (item) {
            if (!item || !item.key) {
                return;
            }

            var isOld = item.submittedAt && now - item.submittedAt > MAX_PENDING_AGE_MS;
            var leftSubmittedPage = item.pageKey && item.pageKey !== pageKey;
            var shouldClear = isOld || leftSubmittedPage || currentPageHasSuccess;

            if (shouldClear) {
                try {
                    window.sessionStorage.removeItem(item.key);
                } catch (error) {
                    // Ignore cleanup failure.
                }
                return;
            }

            remaining.push(item);
        });

        writePendingSubmits(remaining);
    }

    function markFormSubmitted(form) {
        if (!shouldPersistForm(form)) {
            return;
        }

        saveForm(form);

        var key = getFormKey(form);
        var pending = readPendingSubmits().filter(function (item) {
            return item && item.key !== key;
        });

        pending.push({
            key: key,
            pageKey: getPageKey(),
            submittedAt: Date.now()
        });

        writePendingSubmits(pending);
    }

    function bindForm(form) {
        if (!form || form.dataset.formPersistBound === "true") {
            return;
        }

        if (isExplicitlyDisabled(form)) {
            form.dataset.formPersistBound = "true";
            clearFormStorage(form);
            return;
        }

        if (!shouldPersistForm(form)) {
            return;
        }

        form.dataset.formPersistBound = "true";
        restoreForm(form);

        form.addEventListener("input", function (event) {
            if (isPersistableField(event.target)) {
                saveForm(form);
            }
        }, true);

        form.addEventListener("change", function (event) {
            if (isPersistableField(event.target)) {
                saveForm(form);
            }
        }, true);

        form.addEventListener("submit", function (event) {
            if (!event.defaultPrevented) {
                markFormSubmitted(form);
            }
        });
    }

    function bindAllForms() {
        Array.prototype.forEach.call(document.querySelectorAll("form"), bindForm);
    }

    if (originalSubmit) {
        window.HTMLFormElement.prototype.submit = function () {
            try {
                markFormSubmitted(this);
            } catch (error) {
                // Do not block native submission.
            }

            return originalSubmit.apply(this, arguments);
        };
    }

    document.addEventListener("DOMContentLoaded", function () {
        cleanupPendingSubmits();
        bindAllForms();

        if (window.MutationObserver) {
            var observer = new MutationObserver(function (mutations) {
                mutations.forEach(function (mutation) {
                    Array.prototype.forEach.call(mutation.addedNodes, function (node) {
                        if (!node || node.nodeType !== 1) {
                            return;
                        }

                        if (node.matches && node.matches("form")) {
                            bindForm(node);
                        }

                        if (node.querySelectorAll) {
                            Array.prototype.forEach.call(node.querySelectorAll("form"), bindForm);
                        }
                    });
                });
            });

            observer.observe(document.documentElement, { childList: true, subtree: true });
        }
    });
})();
