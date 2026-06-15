document.addEventListener("DOMContentLoaded", function () {
    window.appNotifications = window.appNotifications || {};

    window.appNotifications.show = function (message, type) {
        if (!message || !String(message).trim()) {
            return;
        }

        if (!window.appToast || typeof window.appToast.show !== "function") {
            return;
        }

        window.appToast.show(message, type);
    };
});
