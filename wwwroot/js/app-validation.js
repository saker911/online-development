document.addEventListener("DOMContentLoaded", function () {
    function showNotification(message, type) {
        if (window.appNotifications && typeof window.appNotifications.show === "function") {
            window.appNotifications.show(message, type);
            return;
        }

        if (!message || !String(message).trim()) {
            return;
        }

        if (!window.appToast || typeof window.appToast.show !== "function") {
            return;
        }

        window.appToast.show(message, type);
    }

    function getFieldLabel(field) {
        if (!field) {
            return "";
        }

        if (field.id) {
            var label = document.querySelector("label[for='" + field.id + "']");
            var labelText = (label?.textContent || "").replace(/[\r\n\t*]+/g, " ").trim();
            if (labelText) {
                return labelText;
            }
        }

        return (field.getAttribute("aria-label")
            || field.getAttribute("data-field-label")
            || field.getAttribute("placeholder")
            || field.name
            || "")
            .replace(/[\r\n\t*]+/g, " ")
            .trim();
    }

    function findFieldValidationNode(form, field) {
        if (!form || !field) {
            return null;
        }

        if (field.name) {
            var exactNode = Array.from(form.querySelectorAll("[data-valmsg-for]"))
                .find(function (node) {
                    return node.getAttribute("data-valmsg-for") === field.name;
                });
            if (exactNode) {
                return exactNode;
            }
        }

        var wrapper = field.closest(".form-group, .mb-3, .col, .col-md-6, .col-lg-4, .col-lg-6, .col-12, .ue-field, .app-field, .input-shell, .date-input-shell, .form-floating")
            || field.parentElement;
        if (!wrapper) {
            return null;
        }

        return wrapper.querySelector(".invalid-feedback, .field-validation-error, .text-danger, [data-valmsg-for]");
    }

    function getSingleFieldValidationToastMessage(form, field) {
        if (!field) {
            return "تحقق من الحقول المدخلة.";
        }

        var messageNode = findFieldValidationNode(form, field);
        var messageText = (messageNode?.textContent || "").replace(/[\r\n\t]+/g, " ").trim();
        if (messageText) {
            return messageText;
        }

        var label = getFieldLabel(field);
        if (field.validity?.valueMissing && label) {
            return label + " مطلوب.";
        }

        var nativeMessage = (field.validationMessage || "").trim();
        if (nativeMessage) {
            return nativeMessage;
        }

        return label ? label + " غير مكتمل." : "تحقق من الحقول المدخلة.";
    }

    function buildValidationToastMessage(form, invalidFields) {
        if (!invalidFields || invalidFields.length === 0) {
            return "تحقق من الحقول المدخلة.";
        }

        if (invalidFields.length === 1) {
            return getSingleFieldValidationToastMessage(form, invalidFields[0]);
        }

        return "أكمل الحقول الإلزامية المطلوبة.";
    }

    document.querySelectorAll("form.needs-validation").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            var submitter = event.submitter;
            var skipValidation = submitter?.formNoValidate || submitter?.dataset.skipValidation === "true";

            if (!skipValidation && !form.checkValidity()) {
                event.preventDefault();
                event.stopPropagation();
                form.classList.add("was-validated");
                var invalidFields = Array.from(form.elements).filter(function (el) {
                    return el && el.willValidate && !el.checkValidity();
                });
                showNotification(buildValidationToastMessage(form, invalidFields), "danger");

                var firstInvalidField = invalidFields[0];
                if (firstInvalidField && typeof firstInvalidField.focus === "function") {
                    firstInvalidField.focus();
                }
                return;
            }

            if (!skipValidation) {
                form.classList.add("was-validated");
            }
        });

        form.querySelectorAll("input, select, textarea").forEach(function (field) {
            field.addEventListener("input", function () {
                if (field.checkValidity()) {
                    field.classList.remove("is-invalid");
                }
            });

            field.addEventListener("invalid", function () {
                field.classList.add("is-invalid");
            });
        });
    });

    function processValidationSummaries() {
        var summarySelectors = [".form-summary-only", ".validation-summary-errors", ".alert.alert-danger"];
        summarySelectors.forEach(function (sel) {
            document.querySelectorAll(sel).forEach(function (div) {
                try {
                    var lis = div.querySelectorAll("li");
                    if (!lis || lis.length === 0) return;

                    lis.forEach(function (li) {
                        var text = (li.textContent || "").trim();
                        var m = text.match(/^The\s+([A-Za-z0-9_]+)\s+field\s+/i);
                        if (m && m[1]) {
                            var field = m[1];
                            var selector = "input[name$='." + field + "'], select[name$='." + field + "'], textarea[name$='." + field + "'], input[name='" + field + "'], select[name='" + field + "'], textarea[name='" + field + "'], input[id$='" + field + "'], select[id$='" + field + "'], textarea[id$='" + field + "']";
                            document.querySelectorAll(selector).forEach(function (el) {
                                el.classList.add("is-invalid");
                            });
                        }
                    });

                    div.style.display = "none";
                } catch (e) {
                    // ignore
                }
            });
        });
    }

    processValidationSummaries();

    document.querySelectorAll('form[data-preserve-scroll-on-submit="true"]').forEach(function (form) {
        var storageKey = form.id
            ? "scroll-preserve:" + window.location.pathname + ":" + form.id
            : "scroll-preserve:" + window.location.pathname + ":form";

        try {
            window.history.scrollRestoration = "manual";
        } catch (error) {
            // Ignore browsers that do not allow manual scroll restoration.
        }

        form.addEventListener("submit", function () {
            try {
                window.sessionStorage.setItem(storageKey, String(window.scrollY || window.pageYOffset || 0));
            } catch (error) {
                // Ignore storage errors and allow normal navigation.
            }
        });

        var savedScroll = null;
        try {
            savedScroll = window.sessionStorage.getItem(storageKey);
        } catch (error) {
            savedScroll = null;
        }

        if (savedScroll === null) {
            return;
        }

        var targetY = Number(savedScroll) || 0;
        var restoreScroll = function () {
            window.scrollTo(0, targetY);
        };

        requestAnimationFrame(function () {
            requestAnimationFrame(restoreScroll);
        });
        setTimeout(restoreScroll, 0);

        try {
            window.sessionStorage.removeItem(storageKey);
        } catch (error) {
            // Ignore cleanup errors.
        }
    });

    window.appFormValidation = {
        buildToastMessage: buildValidationToastMessage,
        getSingleFieldToastMessage: getSingleFieldValidationToastMessage
    };

    function toAsciiDigits(s) {
        if (!s) return s;
        return s.replace(/[\u0660-\u0669]/g, function (d) { return String.fromCharCode(d.charCodeAt(0) - 0x0660 + 48); })
            .replace(/[\u06F0-\u06F9]/g, function (d) { return String.fromCharCode(d.charCodeAt(0) - 0x06F0 + 48); });
    }

    function isAllowedPlateFormat(value, origin) {
        if (!value) return false;
        var cleaned = (value || "").replace(/[\s-]/g, "");
        var normalized = toAsciiDigits(cleaned);
        origin = origin || "Saudi";
        var localRe = /^[A-Za-z\u0621-\u064A]{3}\d{4}$/u;
        if (origin === "Saudi") {
            return localRe.test(normalized);
        }
        var foreignRe = /^[A-Za-z\u0621-\u064A0-9]{1,12}$/u;
        return foreignRe.test(normalized);
    }

    document.querySelectorAll(".permit-form").forEach(function (form) {
        form.addEventListener("submit", function (ev) {
            if (ev.defaultPrevented) {
                return;
            }

            if (!form.checkValidity()) {
                ev.preventDefault();
                form.classList.add("was-validated");
                var firstInvalid = null;
                Array.from(form.elements).forEach(function (el) {
                    if (el.willValidate && !el.checkValidity()) {
                        el.classList.add("is-invalid");
                        if (!firstInvalid) firstInvalid = el;
                    }
                });
                showNotification(buildValidationToastMessage(form, firstInvalid ? [firstInvalid].concat([]) : []), "danger");
                if (firstInvalid && typeof firstInvalid.focus === "function") firstInvalid.focus();
                return;
            }

            var plate = form.querySelector('input[name="PlateNumber"]');
            if (plate) {
                var originSelect = form.querySelector('select[name="PlateOrigin"]') || document.getElementById("PlateOriginSelect");
                var origin = originSelect ? (originSelect.value || "Saudi") : "Saudi";
                if (!isAllowedPlateFormat(plate.value, origin)) {
                    ev.preventDefault();
                    plate.classList.add("is-invalid");
                    if (origin === "Saudi") {
                        showNotification("رقم اللوحة السعودية يجب أن يكون 3 أحرف متبوعة بـ4 أرقام (مثال: ABC1234).", "warning");
                    } else {
                        showNotification("لوحة غير سعودية: اقبل أحرف أو أرقام أو مزيج منهما (حتى 12 محرف).", "warning");
                    }
                    plate.focus();
                    return;
                }
            }
        });
    });

    var plateOriginSelect = document.getElementById("PlateOriginSelect");
    var plateInput = document.getElementById("PlateNumberInput");
    var plateHelp = document.getElementById("PlateHelp");
    if (plateOriginSelect && plateInput) {
        plateOriginSelect.addEventListener("change", function () {
            var origin = plateOriginSelect.value || "Saudi";
            if (origin === "Saudi") {
                plateInput.placeholder = "ل ح م 1236 أو ABC 1234";
                if (plateHelp) plateHelp.textContent = "اكتبها مثل: ل ح م 1236 أو ABC 1234، وسيتم حفظها تلقائيا بصيغة موحدة.";
            } else {
                plateInput.placeholder = "لوحة دولة أخرى - اكتب أحرف أو أرقام أو كلاهما";
                if (plateHelp) plateHelp.textContent = "اللوحة غير سعودية مسموح فيها أحرف وأرقام أو أي تركيبة.";
            }
            plateInput.classList.remove("is-invalid");
        });

        plateOriginSelect.dispatchEvent(new Event("change"));
    }
});
