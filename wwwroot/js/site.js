document.addEventListener("DOMContentLoaded", function () {
    var topbar = document.querySelector(".app-topbar");
    if (!topbar) {
        return;
    }

    var scrollParent = topbar.closest(".app-main-scroll") || window;
    var threshold = 12;

    function onScroll() {
        var y = scrollParent === window
            ? window.scrollY
            : scrollParent.scrollTop;
        topbar.classList.toggle("is-scrolled", y > threshold);
    }

    (scrollParent === window ? window : scrollParent)
        .addEventListener("scroll", onScroll, { passive: true });
    onScroll();
});

document.querySelectorAll("[data-delegation-date-input]").forEach(function (input) {
    var key = input.getAttribute("data-delegation-date-input");
    var preview = document.querySelector('[data-delegation-date-preview="' + key + '"]');

    if (!preview) {
        return;
    }

    var updatePreview = function () {
        if (!input.value) {
            preview.textContent = "اختر التاريخ والوقت";
            return;
        }

        var value = new Date(input.value);
        if (Number.isNaN(value.getTime())) {
            preview.textContent = "";
            return;
        }

        var formattedValue = new Intl.DateTimeFormat("ar-SA-u-ca-gregory", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        }).format(value);

        preview.replaceChildren();
        preview.append(document.createTextNode("التاريخ المختار: "));
        var formattedValueElement = document.createElement("span");
        formattedValueElement.className = "delegation-date-preview-value";
        formattedValueElement.dir = "ltr";
        formattedValueElement.textContent = formattedValue;
        preview.append(formattedValueElement);
    };

    input.addEventListener("input", updatePreview);
    input.addEventListener("change", updatePreview);
    updatePreview();
});

document.addEventListener("DOMContentLoaded", function () {
    var workdaySummary = document.querySelector(".administration-workdays-hint");
    var workdayCheckboxes = Array.from(
        document.querySelectorAll(".official-workday-checkbox")
    );

    if (!workdaySummary || workdayCheckboxes.length === 0) {
        return;
    }

    var workdayLabels = {
        Saturday: "السبت",
        Sunday: "الأحد",
        Monday: "الاثنين",
        Tuesday: "الثلاثاء",
        Wednesday: "الأربعاء",
        Thursday: "الخميس",
        Friday: "الجمعة"
    };

    function updateWorkdaySummary() {
        var selectedLabels = workdayCheckboxes
            .filter(function (checkbox) { return checkbox.checked; })
            .map(function (checkbox) {
                return workdayLabels[checkbox.value] || checkbox.value;
            });

        workdaySummary.textContent = selectedLabels.length
            ? selectedLabels.join("، ")
            : "لم يتم التحديد بعد";
    }

    workdayCheckboxes.forEach(function (checkbox) {
        checkbox.addEventListener("change", updateWorkdaySummary);
    });
    updateWorkdaySummary();
});

document.addEventListener("DOMContentLoaded", function () {
    var generatedId = 0;

    function ensureFieldId(field) {
        if (!field.id) {
            generatedId += 1;
            field.id = "form-field-" + generatedId;
        }

        return field.id;
    }

    document.querySelectorAll("label[for]").forEach(function (label) {
        var targetName = label.htmlFor;
        if (!targetName || document.getElementById(targetName)) {
            return;
        }

        var matchingFields = Array.from(document.querySelectorAll("[name]"))
            .filter(function (field) { return field.name === targetName; });

        if (matchingFields.length === 1) {
            label.htmlFor = ensureFieldId(matchingFields[0]);
        }
    });

    document.querySelectorAll("label:not([for])").forEach(function (label) {
        if (label.querySelector("input, select, textarea")) {
            return;
        }

        var container = label.parentElement;
        var field = container
            ? container.querySelector("input:not([type='hidden']), select, textarea")
            : null;

        if (field) {
            label.htmlFor = ensureFieldId(field);
        }
    });
});
