document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll("[data-copy-permit-link]").forEach(function (button) {
        button.addEventListener("click", async function () {
            const shareUrl = button.dataset.shareUrl || window.location.href;
            const originalLabel = button.textContent;
            try {
                await navigator.clipboard.writeText(shareUrl);
                button.textContent = "تم النسخ";
                if (window.appToast) window.appToast.success("تم نسخ رابط المستفيد.");
                window.setTimeout(function () {
                    button.textContent = originalLabel;
                }, 1800);
            }
            catch {
                const input = button.parentElement?.querySelector("input");
                input?.select();
                if (window.appToast) window.appToast.error("حدد الرابط وانسخه يدويًا.");
            }
        });
    });

    document.querySelectorAll("[data-share-permit]").forEach(function (button) {
        button.addEventListener("click", async function () {
            const shareUrl = button.dataset.shareUrl || window.location.href;
            const shareTitle = button.dataset.shareTitle || document.title;
            const status = button.parentElement?.querySelector("[data-share-status]");

            try {
                if (navigator.share) {
                    await navigator.share({ title: shareTitle, url: shareUrl });
                    if (status) status.textContent = "تمت مشاركة رابط التصريح.";
                    return;
                }

                await navigator.clipboard.writeText(shareUrl);
                if (status) status.textContent = "تم نسخ رابط التصريح.";
                if (window.appToast) window.appToast.success("تم نسخ رابط التصريح.");
            }
            catch (error) {
                if (error && error.name === "AbortError") return;
                if (status) status.textContent = "تعذر نسخ الرابط. افتح البطاقة ثم انسخ عنوانها.";
                if (window.appToast) window.appToast.error("تعذر مشاركة رابط التصريح.");
            }
        });
    });
});
