document.addEventListener("DOMContentLoaded", function () {
    function formatDuration(value) {
        const months = Number.parseInt(value, 10);
        if (!Number.isFinite(months) || months < 1) {
            return "أدخل مدة صحيحة";
        }
        if (months === 1) {
            return "اشتراك لمدة شهر واحد";
        }
        if (months === 2) {
            return "اشتراك لمدة شهرين";
        }
        if (months >= 3 && months <= 10) {
            return `اشتراك لمدة ${months} أشهر`;
        }
        if (months === 12) {
            return "اشتراك سنوي كامل";
        }
        if (months === 24) {
            return "اشتراك لمدة سنتين";
        }
        return `اشتراك لمدة ${months} شهرًا`;
    }

    document.querySelectorAll("[data-duration-months]").forEach(function (input) {
        const preview = input.closest(".field-group")?.querySelector("[data-duration-preview]");
        if (!preview) {
            return;
        }

        const sync = function () {
            preview.textContent = formatDuration(input.value);
        };
        input.addEventListener("input", sync);
        input.addEventListener("change", sync);
        sync();
    });
});
