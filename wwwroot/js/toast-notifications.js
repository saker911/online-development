(function () {
    var activeToastKeys = new Set();

    function normalizeToastType(type) {
        return type === "error" ? "danger" : (type || "warning");
    }

    function buildToastKey(message, type) {
        return normalizeToastType(type) + "::" + ((message || "").trim());
    }

    function bindToastLifecycle(notify, toastKey) {
        if (!notify) {
            return;
        }

        if (notify.dataset.toastBound === "true") {
            return;
        }

        notify.dataset.toastBound = "true";
        var dismissed = false;

        function dismiss() {
            if (dismissed) {
                return;
            }

            dismissed = true;
            notify.classList.add("is-leaving");
            activeToastKeys.delete(toastKey);
            setTimeout(function () { notify.remove(); }, 220);
        }

        var timer = setTimeout(dismiss, 5000);
        var closeBtn = notify.querySelector(".app-notify-close");
        if (closeBtn) {
            closeBtn.addEventListener("click", function () {
                clearTimeout(timer);
                dismiss();
            });
        }
    }

    function bindExistingNotifications() {
        document.querySelectorAll(".app-notify").forEach(function (notify, index) {
            var text = notify.querySelector(".app-notify-text");
            var message = text ? text.textContent : notify.textContent;
            var typeClass = Array.prototype.find.call(notify.classList, function (className) {
                return className.indexOf("app-notify-") === 0 && className !== "app-notify";
            });
            var type = typeClass ? typeClass.replace("app-notify-", "") : "info";
            bindToastLifecycle(notify, buildToastKey(message || String(index), type));
        });
    }

    function ensureNotifyContainer() {
        var container = document.querySelector(".app-notify-container");
        if (!container) {
            container = document.createElement("div");
            container.className = "app-notify-container";
            document.body.appendChild(container);
        }
        return container;
    }

    function showNotification(message, type) {
        if (!message || !String(message).trim()) {
            return;
        }

        type = normalizeToastType(type || "warning");
        var toastKey = buildToastKey(message, type);
        if (activeToastKeys.has(toastKey)) {
            return;
        }

        activeToastKeys.add(toastKey);
        var container = ensureNotifyContainer();
        var note = document.createElement("div");
        note.className = "app-notify app-notify-" + type;
        note.setAttribute("role", "alert");
        note.setAttribute("aria-live", "polite");

        var icon = document.createElement("div");
        icon.className = "app-notify-icon";
        icon.textContent = type === "danger" ? "!" : (type === "success" ? "✓" : (type === "warning" ? "⚠" : "ℹ"));

        var text = document.createElement("div");
        text.className = "app-notify-text";
        text.textContent = message;

        var closeBtn = document.createElement("button");
        closeBtn.className = "app-notify-close";
        closeBtn.setAttribute("aria-label", "إغلاق");
        closeBtn.innerHTML = "×";

        var bar = document.createElement("div");
        bar.className = "app-notify-bar";

        note.appendChild(icon);
        note.appendChild(text);
        note.appendChild(closeBtn);
        note.appendChild(bar);

        container.appendChild(note);
        bindToastLifecycle(note, toastKey);
    }

    window.appToast = {
        show: showNotification,
        success: function (message) { showNotification(message, "success"); },
        error: function (message) { showNotification(message, "danger"); },
        warning: function (message) { showNotification(message, "warning"); },
        info: function (message) { showNotification(message, "info"); }
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", bindExistingNotifications, { once: true });
    } else {
        bindExistingNotifications();
    }
})();
