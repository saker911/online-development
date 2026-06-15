document.addEventListener("DOMContentLoaded", function () {
    function normalizePickerText(value) {
        return (value || "").trim().replace(/\s+/g, " ").toLowerCase();
    }

    document.querySelectorAll("select[data-smart-select]").forEach(function (select) {
        if (select.dataset.smartSelectReady === "true") {
            return;
        }

        const form = select.closest("form");
        const placeholder = select.dataset.smartSelectPlaceholder || "ابحث أو اختر";
        const inputId = `${select.id || select.name || "smart-select"}-picker-input`;
        const listId = `${inputId}-list`;
        const wrapper = document.createElement("div");
        wrapper.className = "smart-select";
        wrapper.setAttribute("data-smart-select-wrapper", "true");

        select.classList.add("smart-select-native");
        select.dataset.smartSelectReady = "true";
        select.parentNode.insertBefore(wrapper, select);
        wrapper.appendChild(select);

        wrapper.insertAdjacentHTML("beforeend", `
            <div class="smart-select-control">
                <input type="text" id="${inputId}" class="form-control smart-select-input"
                    placeholder="${placeholder}" autocomplete="off" role="combobox"
                    aria-autocomplete="list" aria-expanded="false" aria-haspopup="listbox"
                    aria-controls="${listId}" />
                <button type="button" class="smart-select-toggle" aria-label="فتح قائمة الخيارات"></button>
            </div>
            <div class="smart-select-panel" hidden>
                <div class="smart-select-options" id="${listId}" role="listbox"></div>
                <div class="smart-select-empty small text-muted-ar" hidden>لا توجد نتائج مطابقة.</div>
            </div>
        `);

        const searchInput = wrapper.querySelector(".smart-select-input");
        const toggleButton = wrapper.querySelector(".smart-select-toggle");
        const panel = wrapper.querySelector(".smart-select-panel");
        const optionsHost = wrapper.querySelector(".smart-select-options");
        const emptyState = wrapper.querySelector(".smart-select-empty");
        let activeOptionIndex = -1;

        const optionButtons = Array.from(select.options).map(function (option, index) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "smart-select-option";
            button.dataset.value = option.value;
            button.dataset.label = (option.textContent || "").trim();
            button.dataset.index = String(index);
            button.id = `${listId}-option-${index}`;
            button.setAttribute("role", "option");
            button.textContent = (option.textContent || "").trim();
            optionsHost.appendChild(button);
            return button;
        });

        function visibleOptions() {
            return optionButtons.filter(function (option) {
                return !option.hidden;
            });
        }

        function getDisplayLabel(option) {
            if (!option) {
                return "";
            }

            if (option.value === "" && select.required) {
                return "";
            }

            return option.dataset.label || "";
        }

        function setOpen(isOpen) {
            if (select.disabled) {
                isOpen = false;
            }

            panel.hidden = !isOpen;
            wrapper.classList.toggle("is-open", isOpen);
            searchInput.setAttribute("aria-expanded", isOpen ? "true" : "false");
        }

        function getSelectedButton() {
            return optionButtons.find(function (option) {
                return option.classList.contains("is-selected");
            }) || null;
        }

        function setActiveOption(option) {
            optionButtons.forEach(function (item) {
                item.classList.remove("is-active");
            });

            if (!option || option.hidden) {
                activeOptionIndex = -1;
                searchInput.removeAttribute("aria-activedescendant");
                return;
            }

            option.classList.add("is-active");
            activeOptionIndex = optionButtons.indexOf(option);
            searchInput.setAttribute("aria-activedescendant", option.id);
            option.scrollIntoView({ block: "nearest" });
        }

        function syncSelectedOption() {
            let matchedOption = null;

            optionButtons.forEach(function (option) {
                const isSelected = option.dataset.value === select.value;
                option.classList.toggle("is-selected", isSelected);
                option.setAttribute("aria-selected", isSelected ? "true" : "false");
                if (isSelected) {
                    matchedOption = option;
                }
            });

            searchInput.value = getDisplayLabel(matchedOption);

            if (matchedOption) {
                setActiveOption(matchedOption);
                return;
            }

            setActiveOption(null);
        }

        function filterOptions(query) {
            const needle = normalizePickerText(query);
            let firstVisibleOption = null;

            optionButtons.forEach(function (option) {
                const label = option.dataset.label || "";
                const matches = !needle || normalizePickerText(label).includes(needle);
                option.hidden = !matches;
                option.disabled = !matches;
                if (matches && !firstVisibleOption) {
                    firstVisibleOption = option;
                }
            });

            if (emptyState) {
                emptyState.hidden = !!firstVisibleOption;
            }

            if (!firstVisibleOption) {
                setActiveOption(null);
                return;
            }

            setActiveOption(getSelectedButton() || firstVisibleOption);
        }

        function commitSelection(option, shouldClose) {
            select.value = option.dataset.value || "";
            searchInput.value = getDisplayLabel(option);
            syncSelectedOption();
            searchInput.setCustomValidity("");
            searchInput.classList.remove("is-invalid");
            select.dispatchEvent(new Event("input", { bubbles: true }));
            select.dispatchEvent(new Event("change", { bubbles: true }));

            if (shouldClose !== false) {
                setOpen(false);
            }
        }

        function findExactOption(text) {
            const normalizedText = normalizePickerText(text);
            if (!normalizedText) {
                return optionButtons.find(function (option) {
                    return option.dataset.value === "";
                }) || null;
            }

            return optionButtons.find(function (option) {
                return normalizePickerText(option.dataset.label || "") === normalizedText;
            }) || null;
        }

        function ensureValidSelection() {
            searchInput.setCustomValidity("");
            searchInput.classList.remove("is-invalid");

            if (select.disabled) {
                return true;
            }

            const typedValue = (searchInput.value || "").trim();
            if (!typedValue) {
                if (select.required) {
                    select.value = "";
                    searchInput.setCustomValidity("يرجى اختيار قيمة من القائمة.");
                    return false;
                }

                const emptyOption = findExactOption("");
                if (emptyOption) {
                    commitSelection(emptyOption, false);
                }
                return true;
            }

            const exactOption = findExactOption(typedValue);
            if (!exactOption || (exactOption.dataset.value === "" && select.required)) {
                searchInput.setCustomValidity("اختر قيمة من القائمة.");
                return false;
            }

            commitSelection(exactOption, false);
            return true;
        }

        function syncDisabledState() {
            const isDisabled = !!select.disabled;
            searchInput.disabled = isDisabled;
            toggleButton.disabled = isDisabled;
            wrapper.classList.toggle("is-disabled", isDisabled);
            if (isDisabled) {
                setOpen(false);
            }
        }

        optionButtons.forEach(function (option) {
            option.addEventListener("click", function () {
                commitSelection(option);
            });
        });

        searchInput.addEventListener("focus", function () {
            filterOptions("");
            setOpen(true);
        });

        searchInput.addEventListener("click", function () {
            filterOptions("");
            setOpen(true);
        });

        searchInput.addEventListener("input", function () {
            searchInput.setCustomValidity("");
            searchInput.classList.remove("is-invalid");
            filterOptions(searchInput.value);
            setOpen(true);
        });

        searchInput.addEventListener("blur", function () {
            window.setTimeout(function () {
                if (wrapper.contains(document.activeElement)) {
                    return;
                }

                if (!ensureValidSelection()) {
                    searchInput.classList.add("is-invalid");
                }
                setOpen(false);
            }, 120);
        });

        searchInput.addEventListener("keydown", function (event) {
            const currentVisibleOptions = visibleOptions();

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                if (panel.hidden) {
                    filterOptions(searchInput.value);
                    setOpen(true);
                }

                if (!currentVisibleOptions.length) {
                    return;
                }

                let nextIndex = currentVisibleOptions.findIndex(function (option) {
                    return option.classList.contains("is-active");
                });

                if (nextIndex === -1) {
                    nextIndex = 0;
                } else {
                    nextIndex = event.key === "ArrowDown"
                        ? Math.min(nextIndex + 1, currentVisibleOptions.length - 1)
                        : Math.max(nextIndex - 1, 0);
                }

                setActiveOption(currentVisibleOptions[nextIndex]);
                return;
            }

            if (event.key === "Enter") {
                if (panel.hidden) {
                    return;
                }

                event.preventDefault();
                const activeOption = optionButtons[activeOptionIndex] || currentVisibleOptions[0];
                if (activeOption) {
                    commitSelection(activeOption);
                }
                return;
            }

            if (event.key === "Escape") {
                if (!panel.hidden) {
                    event.preventDefault();
                    setOpen(false);
                }
                return;
            }

            if (event.key === "Tab") {
                ensureValidSelection();
            }
        });

        toggleButton.addEventListener("click", function (event) {
            event.preventDefault();
            if (select.disabled) {
                return;
            }

            if (panel.hidden) {
                filterOptions("");
                setOpen(true);
                searchInput.focus();
                return;
            }

            setOpen(false);
        });

        document.addEventListener("click", function (event) {
            if (wrapper.contains(event.target)) {
                return;
            }

            if (!panel.hidden) {
                ensureValidSelection();
                setOpen(false);
            }
        });

        if (form) {
            form.addEventListener("submit", function () {
                if (!ensureValidSelection()) {
                    searchInput.classList.add("is-invalid");
                }
            });
        }

        const disabledObserver = new MutationObserver(syncDisabledState);
        disabledObserver.observe(select, { attributes: true, attributeFilter: ["disabled"] });

        select.addEventListener("change", syncSelectedOption);
        searchInput.placeholder = placeholder;
        syncSelectedOption();
        syncDisabledState();
    });

    document.querySelectorAll("[data-department-picker]").forEach(function (picker) {
        const valueInput = picker.querySelector("[data-department-picker-value]");
        const searchInput = picker.querySelector("[data-department-picker-input]");
        const toggleButton = picker.querySelector("[data-department-picker-toggle]");
        const panel = picker.querySelector("[data-department-picker-panel]");
        const emptyState = picker.querySelector("[data-department-picker-empty]");
        const options = Array.from(picker.querySelectorAll("[data-department-picker-option]"));
        const form = picker.closest("form");
        let activeOptionIndex = -1;

        if (!valueInput || !searchInput || !panel || options.length === 0) {
            return;
        }

        options.forEach(function (option, index) {
            if (!option.id) {
                option.id = `${searchInput.id || "department-picker"}-option-${index}`;
            }
        });

        function getOptionLabel(option) {
            return (option?.dataset.value || option?.textContent || "").trim();
        }

        function visibleOptions() {
            return options.filter(function (option) {
                return !option.hidden;
            });
        }

        function setOpen(isOpen) {
            if (searchInput.disabled) {
                isOpen = false;
            }

            panel.hidden = !isOpen;
            picker.classList.toggle("is-open", isOpen);
            searchInput.setAttribute("aria-expanded", isOpen ? "true" : "false");
        }

        function setActiveOption(option) {
            options.forEach(function (item) {
                item.classList.remove("is-active");
            });

            if (!option || option.hidden) {
                activeOptionIndex = -1;
                searchInput.removeAttribute("aria-activedescendant");
                return;
            }

            option.classList.add("is-active");
            activeOptionIndex = options.indexOf(option);
            searchInput.setAttribute("aria-activedescendant", option.id);
            option.scrollIntoView({ block: "nearest" });
        }

        function syncSelectedOption(selectedValue) {
            const normalizedValue = normalizePickerText(selectedValue);
            let matchedOption = null;

            options.forEach(function (option) {
                const isSelected = normalizedValue !== "" && normalizePickerText(getOptionLabel(option)) === normalizedValue;
                option.classList.toggle("is-selected", isSelected);
                option.setAttribute("aria-selected", isSelected ? "true" : "false");
                if (isSelected) {
                    matchedOption = option;
                }
            });

            if (matchedOption) {
                setActiveOption(matchedOption);
            }
        }

        function filterOptions(query) {
            const needle = normalizePickerText(query);
            let firstVisibleOption = null;

            options.forEach(function (option) {
                const matches = !needle || normalizePickerText(getOptionLabel(option)).includes(needle);
                option.hidden = !matches;
                option.disabled = !matches;
                if (matches && !firstVisibleOption) {
                    firstVisibleOption = option;
                }
            });

            if (emptyState) {
                emptyState.hidden = !!firstVisibleOption;
            }

            if (!firstVisibleOption) {
                setActiveOption(null);
                return;
            }

            const selectedVisibleOption = visibleOptions().find(function (option) {
                return option.classList.contains("is-selected");
            });

            setActiveOption(selectedVisibleOption || firstVisibleOption);
        }

        function clearValidationState() {
            searchInput.setCustomValidity("");
            searchInput.classList.remove("is-invalid");
        }

        function dispatchValueChange() {
            valueInput.dispatchEvent(new Event("input", { bubbles: true }));
            valueInput.dispatchEvent(new Event("change", { bubbles: true }));
        }

        function commitSelection(option, shouldClose) {
            const nextValue = getOptionLabel(option);
            valueInput.value = nextValue;
            searchInput.value = nextValue;
            searchInput.dataset.lastCommittedValue = nextValue;
            clearValidationState();
            syncSelectedOption(nextValue);
            dispatchValueChange();

            if (shouldClose !== false) {
                setOpen(false);
            }
        }

        function clearSelection(shouldNotify) {
            valueInput.value = "";
            searchInput.dataset.lastCommittedValue = "";
            syncSelectedOption("");

            if (shouldNotify) {
                dispatchValueChange();
            }
        }

        function findExactOption(text) {
            const normalizedText = normalizePickerText(text);
            if (!normalizedText) {
                return null;
            }

            return options.find(function (option) {
                return normalizePickerText(getOptionLabel(option)) === normalizedText;
            }) || null;
        }

        function ensureValidSelection() {
            clearValidationState();

            if (searchInput.disabled) {
                return true;
            }

            const typedValue = (searchInput.value || "").trim();
            if (!typedValue) {
                clearSelection(true);
                if (searchInput.required) {
                    searchInput.setCustomValidity("يرجى اختيار القسم.");
                    return false;
                }

                return true;
            }

            const exactOption = findExactOption(typedValue);
            if (!exactOption) {
                clearSelection(true);
                searchInput.setCustomValidity("اختر قسمًا من القائمة.");
                return false;
            }

            commitSelection(exactOption, false);
            return true;
        }

        function syncDisabledState() {
            const isDisabled = !!searchInput.disabled;
            picker.classList.toggle("is-disabled", isDisabled);
            if (toggleButton) {
                toggleButton.disabled = isDisabled;
            }
            if (isDisabled) {
                setOpen(false);
            }
        }

        options.forEach(function (option) {
            option.addEventListener("click", function () {
                commitSelection(option);
            });
        });

        searchInput.addEventListener("focus", function () {
            filterOptions("");
            setOpen(true);
        });

        searchInput.addEventListener("click", function () {
            filterOptions("");
            setOpen(true);
        });

        searchInput.addEventListener("input", function () {
            clearSelection(true);
            clearValidationState();
            filterOptions(searchInput.value);
            setOpen(true);
        });

        searchInput.addEventListener("blur", function () {
            window.setTimeout(function () {
                if (picker.contains(document.activeElement)) {
                    return;
                }

                if (!ensureValidSelection()) {
                    searchInput.classList.add("is-invalid");
                }
                setOpen(false);
            }, 120);
        });

        searchInput.addEventListener("keydown", function (event) {
            const currentVisibleOptions = visibleOptions();

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                if (panel.hidden) {
                    filterOptions(searchInput.value);
                    setOpen(true);
                }

                if (!currentVisibleOptions.length) {
                    return;
                }

                let nextIndex = currentVisibleOptions.findIndex(function (option) {
                    return option.classList.contains("is-active");
                });

                if (nextIndex === -1) {
                    nextIndex = 0;
                } else {
                    nextIndex = event.key === "ArrowDown"
                        ? Math.min(nextIndex + 1, currentVisibleOptions.length - 1)
                        : Math.max(nextIndex - 1, 0);
                }

                setActiveOption(currentVisibleOptions[nextIndex]);
                return;
            }

            if (event.key === "Enter") {
                if (panel.hidden) {
                    return;
                }

                event.preventDefault();
                const activeOption = options[activeOptionIndex] || currentVisibleOptions[0];
                if (activeOption) {
                    commitSelection(activeOption);
                }
                return;
            }

            if (event.key === "Escape") {
                if (!panel.hidden) {
                    event.preventDefault();
                    setOpen(false);
                }
                return;
            }

            if (event.key === "Tab") {
                ensureValidSelection();
            }
        });

        if (toggleButton) {
            toggleButton.addEventListener("click", function (event) {
                event.preventDefault();
                if (searchInput.disabled) {
                    return;
                }

                if (panel.hidden) {
                    filterOptions("");
                    setOpen(true);
                    searchInput.focus();
                    return;
                }

                setOpen(false);
            });
        }

        document.addEventListener("click", function (event) {
            if (picker.contains(event.target)) {
                return;
            }

            if (!panel.hidden) {
                ensureValidSelection();
                setOpen(false);
            }
        });

        if (form) {
            form.addEventListener("submit", function () {
                if (!ensureValidSelection()) {
                    searchInput.classList.add("is-invalid");
                }
            });
        }

        const initialOption = findExactOption(valueInput.value || searchInput.value);
        if (initialOption) {
            const initialValue = getOptionLabel(initialOption);
            valueInput.value = initialValue;
            searchInput.value = initialValue;
            searchInput.dataset.lastCommittedValue = initialValue;
            syncSelectedOption(initialValue);
        } else {
            syncSelectedOption(valueInput.value);
        }

        filterOptions("");
        setOpen(false);
        syncDisabledState();

        const disabledObserver = new MutationObserver(function () {
            syncDisabledState();
        });
        disabledObserver.observe(searchInput, {
            attributes: true,
            attributeFilter: ["disabled"],
        });
    });
});
