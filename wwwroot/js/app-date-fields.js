document.addEventListener("DOMContentLoaded", function () {
    const hijriFormatter = typeof Intl !== "undefined"
        ? new Intl.DateTimeFormat("ar-SA-u-ca-islamic", {
            year: "numeric",
            month: "long",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit",
            hour12: true,
        })
        : null;
    const hijriDateOnlyFormatter = typeof Intl !== "undefined"
        ? new Intl.DateTimeFormat("ar-SA-u-ca-islamic", {
            year: "numeric",
            month: "long",
            day: "2-digit",
        })
        : null;
    const gregorianFormatter = typeof Intl !== "undefined"
        ? new Intl.DateTimeFormat("ar-SA-u-ca-gregory", {
            year: "numeric",
            month: "long",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit",
            hour12: true,
        })
        : null;
    const gregorianDateOnlyFormatter = typeof Intl !== "undefined"
        ? new Intl.DateTimeFormat("ar-SA-u-ca-gregory", {
            year: "numeric",
            month: "long",
            day: "2-digit",
        })
        : null;

    function toHijriText(value, dateOnly) {
        var fmt = dateOnly ? hijriDateOnlyFormatter : hijriFormatter;
        if (!value || !fmt) {
            return "";
        }

        const normalized = value.length === 10 ? value + "T00:00:00" : (value.length === 16 ? value + ":00" : value);
        const date = new Date(normalized);

        if (Number.isNaN(date.getTime())) {
            return "";
        }

        return fmt.format(date);
    }

    function toGregorianText(value, dateOnly) {
        var fmt = dateOnly ? gregorianDateOnlyFormatter : gregorianFormatter;
        if (!value || !fmt) {
            return "";
        }

        const normalized = value.length === 10 ? value + "T00:00:00" : (value.length === 16 ? value + ":00" : value);
        const date = new Date(normalized);

        if (Number.isNaN(date.getTime())) {
            return "";
        }

        return fmt.format(date) + " م";
    }

    function getDateDisplayMode(source) {
        const field = source.closest("[data-date-field]");
        return field?.dataset.dateDisplayMode || "hijri";
    }

    function syncDatePreview(source) {
        const preview = document.querySelector(`[data-hijri-preview-for='${source.id}']`);
        if (!preview) {
            return;
        }

        const mode = getDateDisplayMode(source);
        const dateOnly = source.type === "date";
        const previewText = mode === "gregorian"
            ? toGregorianText(source.value, dateOnly)
            : toHijriText(source.value, dateOnly);
        const emptyText = preview.dataset.emptyText || "يوم/شهر/سنة";
        const displayText = previewText || emptyText;
        if ("value" in preview) {
            preview.value = displayText;
        } else {
            preview.textContent = displayText;
        }
        source.title = previewText || emptyText;
    }

    function toLocalDateTimeInputValue(date) {
        const pad = (value) => String(value).padStart(2, "0");
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    }

    document.querySelectorAll("[data-hijri-source]").forEach(function (source) {
        if (!source.id) {
            return;
        }

        syncDatePreview(source);
        source.addEventListener("input", function () {
            syncDatePreview(source);
        });
        source.addEventListener("change", function () {
            syncDatePreview(source);
        });
    });

    document.querySelectorAll("[data-date-field]").forEach(function (field) {
        const buttons = field.querySelectorAll("[data-date-display-option]");
        const source = field.querySelector("[data-hijri-source]");
        const trigger = field.querySelector("[data-date-display-trigger]");
        const menu = field.querySelector("[data-date-display-menu]");
        const toggleShell = field.querySelector(".date-hijri-toggle");
        const hijriSwitch = field.querySelector("[data-date-hijri-switch]");
        const inputShell = field.querySelector(".date-input-shell");
        const nativeInput = field.querySelector("input[type='datetime-local'], input[type='date']");
        let toggleScaleHijri = null;
        let toggleScaleGregorian = null;

        if (toggleShell) {
            let toggleFooter = field.querySelector(".date-field-mode-footer");

            if (!toggleFooter) {
                toggleFooter = document.createElement("div");
                toggleFooter.className = "date-field-mode-footer";
                if (inputShell && inputShell.parentElement === field) {
                    inputShell.insertAdjacentElement("afterend", toggleFooter);
                } else {
                    field.appendChild(toggleFooter);
                }
            }

            if (toggleShell.parentElement !== toggleFooter) {
                toggleFooter.appendChild(toggleShell);
            }

            const toggleLabel = toggleShell.querySelector(".date-hijri-toggle-label");
            if (toggleLabel) {
                toggleLabel.classList.add("visually-hidden");
            }

            let toggleScale = toggleShell.querySelector(".date-hijri-toggle-scale");
            if (!toggleScale) {
                toggleScale = document.createElement("span");
                toggleScale.className = "date-hijri-toggle-scale";
                toggleScale.setAttribute("aria-hidden", "true");

                const hijriItem = document.createElement("span");
                hijriItem.className = "date-hijri-toggle-scale-item date-hijri-toggle-scale-item-hijri";
                hijriItem.textContent = "هـ";

                const gregorianItem = document.createElement("span");
                gregorianItem.className = "date-hijri-toggle-scale-item date-hijri-toggle-scale-item-gregorian";
                gregorianItem.textContent = "م";

                toggleScale.appendChild(hijriItem);
                toggleScale.appendChild(gregorianItem);
                toggleShell.appendChild(toggleScale);
            }

            toggleScaleHijri = toggleShell.querySelector(".date-hijri-toggle-scale-item-hijri");
            toggleScaleGregorian = toggleShell.querySelector(".date-hijri-toggle-scale-item-gregorian");
        }

        if (inputShell && nativeInput) {
            inputShell.addEventListener("click", function (e) {
                if (e.target.closest(".date-hijri-toggle")) {
                    return;
                }

                if (nativeInput.type === "date") {
                    e.preventDefault();
                    e.stopPropagation();
                    openArabicDatePicker(nativeInput, inputShell);
                    return;
                }

                if (typeof nativeInput.showPicker === "function") {
                    e.preventDefault();
                    e.stopPropagation();
                    try {
                        nativeInput.showPicker();
                        return;
                    } catch (error) {
                        // Fall back to focusing the field when the browser blocks showPicker.
                    }
                }

                nativeInput.focus();
            });

            if (nativeInput.type === "date") {
                nativeInput.addEventListener("click", function (e) { e.preventDefault(); });
                nativeInput.addEventListener("mousedown", function (e) { e.preventDefault(); });
            }

            inputShell.style.cursor = "pointer";
        }

        function setMenuOpen(isOpen) {
            if (!menu || !trigger) {
                return;
            }

            menu.hidden = !isOpen;
            trigger.setAttribute("aria-expanded", isOpen ? "true" : "false");
        }

        function setMode(mode) {
            field.dataset.dateDisplayMode = mode;
            buttons.forEach(function (button) {
                const isActive = button.getAttribute("data-date-display-option") === mode;
                button.classList.toggle("active", isActive);
                button.setAttribute("aria-pressed", isActive ? "true" : "false");
            });

            if (hijriSwitch) {
                hijriSwitch.checked = (mode === "hijri");
            }

            if (toggleShell) {
                toggleShell.dataset.mode = mode;
                toggleShell.setAttribute("title", mode === "hijri" ? "عرض هجري" : "عرض ميلادي");
            }

            if (toggleScaleHijri) {
                toggleScaleHijri.classList.toggle("active", mode === "hijri");
            }

            if (toggleScaleGregorian) {
                toggleScaleGregorian.classList.toggle("active", mode === "gregorian");
            }

            if (trigger) {
                trigger.setAttribute("aria-label", mode === "gregorian" ? "ميلادي" : "هجري");
            }

            setMenuOpen(false);

            if (source) {
                syncDatePreview(source);
            }
        }

        if (trigger && menu) {
            trigger.addEventListener("click", function (event) {
                event.preventDefault();
                setMenuOpen(menu.hidden);
            });

            trigger.title = "اختيار هجري أو ميلادي";
        }

        buttons.forEach(function (button) {
            button.addEventListener("click", function () {
                setMode(button.getAttribute("data-date-display-option") || "hijri");
            });
        });

        if (hijriSwitch) {
            hijriSwitch.addEventListener("change", function () {
                setMode(hijriSwitch.checked ? "hijri" : "gregorian");
            });
        }

        if (menu) {
            menu.addEventListener("click", function (event) {
                event.stopPropagation();
            });
        }

        setMode(field.dataset.dateDisplayMode || "hijri");
    });

    document.querySelectorAll("[data-sync-browser-now]").forEach(function (input) {
        if (!input || input.tagName !== "INPUT") {
            return;
        }

        if (input.dataset.syncBrowserNow !== "true") {
            return;
        }

        const offsetMinutes = Number(input.dataset.syncBrowserNowOffset || "0");
        const browserNow = new Date(Date.now() + (Number.isFinite(offsetMinutes) ? offsetMinutes : 0) * 60000);
        input.value = toLocalDateTimeInputValue(browserNow);
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
    });

    document.querySelectorAll('[data-date-min-today="true"]').forEach(function (input) {
        if (!input || input.tagName !== "INPUT") {
            return;
        }
        if (input.type === "date") {
            var pad = (v) => String(v).padStart(2, "0");
            var now = new Date();
            input.setAttribute("min", `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`);
        } else {
            var todayMin = toLocalDateTimeInputValue(new Date());
            input.setAttribute("min", todayMin);
        }
    });

    document.addEventListener("click", function (event) {
        document.querySelectorAll("[data-date-field]").forEach(function (field) {
            const trigger = field.querySelector("[data-date-display-trigger]");
            const menu = field.querySelector("[data-date-display-menu]");

            if (!trigger || !menu || menu.hidden || field.contains(event.target)) {
                return;
            }

            menu.hidden = true;
            trigger.setAttribute("aria-expanded", "false");
        });
    });

    document.querySelectorAll("[data-hijri-source]").forEach(function (source) {
        if (!source.id) {
            return;
        }

        source.addEventListener("focus", function () {
            const field = source.closest("[data-date-field]");
            if (!field) {
                return;
            }

            const preview = field.querySelector(`[data-hijri-preview-for='${source.id}']`);
            if (preview) {
                preview.title = preview.value || preview.title;
            }
        });
    });

    const arMonths = ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
        "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];
    const arDays = ["ح", "ن", "ث", "ر", "خ", "ج", "س"];

    function toAr(n) { return String(n).replace(/\d/g, function (d) { return "٠١٢٣٤٥٦٧٨٩"[d]; }); }

    var _pickerEl = null;
    var _pickerTarget = null;
    var _pickerYear, _pickerMonth;

    function closePicker() {
        if (_pickerEl) { _pickerEl.remove(); _pickerEl = null; _pickerTarget = null; }
    }

    function openArabicDatePicker(input, anchor) {
        if (_pickerEl && _pickerTarget === input) { closePicker(); return; }
        closePicker();
        _pickerTarget = input;

        var now = new Date();
        if (input.value) {
            var parts = input.value.split("-");
            _pickerYear = parseInt(parts[0], 10);
            _pickerMonth = parseInt(parts[1], 10) - 1;
        } else {
            _pickerYear = now.getFullYear();
            _pickerMonth = now.getMonth();
        }

        _pickerEl = document.createElement("div");
        _pickerEl.className = "ar-datepicker";
        _pickerEl.setAttribute("dir", "rtl");
        renderPicker(input, now);

        document.body.appendChild(_pickerEl);
        positionPicker(anchor);
    }

    function positionPicker(anchor) {
        if (!_pickerEl) return;
        var rect = anchor.getBoundingClientRect();
        var pickerH = _pickerEl.offsetHeight || 340;
        var spaceBelow = window.innerHeight - rect.bottom;
        var showAbove = spaceBelow < pickerH && rect.top > pickerH;

        _pickerEl.style.position = "fixed";
        if (showAbove) {
            _pickerEl.style.top = (rect.top - pickerH - 4) + "px";
        } else {
            _pickerEl.style.top = (rect.bottom + 4) + "px";
        }
        _pickerEl.style.right = (document.documentElement.clientWidth - rect.right) + "px";
        _pickerEl.style.left = "auto";
    }

    function renderPicker(input, today) {
        if (!_pickerEl) return;
        today = today || new Date();
        var minStr = input.getAttribute("min") || "";
        var minDate = minStr ? new Date(minStr + "T00:00:00") : null;

        var pad2 = function (v) { return String(v).padStart(2, "0"); };
        var todayStr = today.getFullYear() + "-" + pad2(today.getMonth() + 1) + "-" + pad2(today.getDate());
        var selStr = input.value || "";

        var firstDay = new Date(_pickerYear, _pickerMonth, 1).getDay();
        var startIdx = (firstDay + 1) % 7;
        var daysInMonth = new Date(_pickerYear, _pickerMonth + 1, 0).getDate();

        var h = '<div class="ar-dp-header">';
        h += '<button type="button" class="ar-dp-nav" data-dir="next">&#x276E;</button>';
        h += '<span class="ar-dp-title">' + arMonths[_pickerMonth] + ' ' + toAr(_pickerYear) + '</span>';
        h += '<button type="button" class="ar-dp-nav" data-dir="prev">&#x276F;</button>';
        h += '</div>';

        h += '<div class="ar-dp-weekdays">';
        for (var i = 0; i < 7; i++) h += '<span>' + arDays[i] + '</span>';
        h += '</div>';

        h += '<div class="ar-dp-days">';
        for (var e = 0; e < startIdx; e++) h += '<span class="ar-dp-empty"></span>';
        for (var d = 1; d <= daysInMonth; d++) {
            var dateStr = _pickerYear + "-" + pad2(_pickerMonth + 1) + "-" + pad2(d);
            var cls = "ar-dp-day";
            var disabled = false;
            if (dateStr === todayStr) cls += " ar-dp-today";
            if (dateStr === selStr) cls += " ar-dp-selected";
            if (minDate && new Date(dateStr + "T00:00:00") < minDate) { cls += " ar-dp-disabled"; disabled = true; }
            h += '<button type="button" class="' + cls + '" data-date="' + dateStr + '"' + (disabled ? ' disabled' : '') + '>' + toAr(d) + '</button>';
        }
        h += '</div>';

        h += '<div class="ar-dp-footer">';
        h += '<button type="button" class="ar-dp-today-btn">اليوم</button>';
        h += '<button type="button" class="ar-dp-clear-btn">مسح</button>';
        h += '</div>';

        _pickerEl.innerHTML = h;

        _pickerEl.querySelectorAll(".ar-dp-nav").forEach(function (btn) {
            btn.addEventListener("click", function (ev) {
                ev.stopPropagation();
                if (btn.dataset.dir === "prev") {
                    _pickerMonth--;
                    if (_pickerMonth < 0) { _pickerMonth = 11; _pickerYear--; }
                } else {
                    _pickerMonth++;
                    if (_pickerMonth > 11) { _pickerMonth = 0; _pickerYear++; }
                }
                renderPicker(input, today);
            });
        });

        _pickerEl.querySelectorAll(".ar-dp-day:not([disabled])").forEach(function (btn) {
            btn.addEventListener("click", function (ev) {
                ev.stopPropagation();
                input.value = btn.dataset.date;
                input.dispatchEvent(new Event("input", { bubbles: true }));
                input.dispatchEvent(new Event("change", { bubbles: true }));
                closePicker();
            });
        });

        var todayBtn = _pickerEl.querySelector(".ar-dp-today-btn");
        if (todayBtn) todayBtn.addEventListener("click", function (ev) {
            ev.stopPropagation();
            if (minDate && today < minDate) return;
            input.value = todayStr;
            input.dispatchEvent(new Event("input", { bubbles: true }));
            input.dispatchEvent(new Event("change", { bubbles: true }));
            closePicker();
        });

        var clearBtn = _pickerEl.querySelector(".ar-dp-clear-btn");
        if (clearBtn) clearBtn.addEventListener("click", function (ev) {
            ev.stopPropagation();
            input.value = "";
            input.dispatchEvent(new Event("input", { bubbles: true }));
            input.dispatchEvent(new Event("change", { bubbles: true }));
            closePicker();
        });

        _pickerEl.addEventListener("click", function (ev) { ev.stopPropagation(); });
    }

    document.addEventListener("click", function () { closePicker(); });
    document.addEventListener("keydown", function (ev) { if (ev.key === "Escape") closePicker(); });
});
