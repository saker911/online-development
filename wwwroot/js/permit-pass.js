document.addEventListener("DOMContentLoaded", function () {
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
