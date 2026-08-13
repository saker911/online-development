document.addEventListener("DOMContentLoaded", () => {
    const trigger = document.querySelector("[data-notification-stream-url]");
    if (!trigger || typeof window.EventSource !== "function") {
        return;
    }

    const badge = trigger.querySelector("[data-notification-badge]");
    const streamUrl = trigger.dataset.notificationStreamUrl;
    const snapshotUrl = trigger.dataset.notificationSnapshotUrl;
    let lastLatestId = null;
    let initialized = false;

    const escapeHtml = (value) => String(value ?? "").replace(/[&<>"']/g, (character) => ({
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        "\"": "&quot;",
        "'": "&#39;"
    })[character]);

    const updateBadge = (count) => {
        if (!badge) return;
        const safeCount = Math.max(0, Number(count) || 0);
        badge.textContent = safeCount > 99 ? "99+" : String(safeCount);
        badge.hidden = safeCount === 0;
    };

    const renderSnapshot = async () => {
        if (!snapshotUrl) return;
        const response = await fetch(snapshotUrl, {
            headers: { "X-Requested-With": "XMLHttpRequest" },
            credentials: "same-origin"
        });
        if (!response.ok) return;
        const snapshot = await response.json();
        updateBadge(snapshot.unreadCount);

        const currentList = document.querySelector("[data-notification-list]");
        const currentEmpty = document.querySelector("[data-notification-empty]");
        if (!currentList && !currentEmpty) return;

        const container = currentList || currentEmpty;
        if (!Array.isArray(snapshot.items) || snapshot.items.length === 0) {
            container.className = "notif-dd-empty";
            container.setAttribute("data-notification-empty", "");
            container.removeAttribute("data-notification-list");
            container.innerHTML = '<div class="notif-dd-empty-msg">لا توجد إشعارات جديدة</div><div class="notif-dd-empty-sub">ستظهر هنا إشعاراتك فور وصولها.</div>';
            return;
        }

        container.className = "notif-dd-body";
        container.setAttribute("data-notification-list", "");
        container.removeAttribute("data-notification-empty");
        container.innerHTML = snapshot.items.map((item) => {
            const url = typeof item.actionUrl === "string" && item.actionUrl.startsWith("/")
                ? item.actionUrl
                : "/Notifications";
            return `<a class="notif-dd-row" href="${escapeHtml(url)}"><span class="notif-dd-dot"></span><span class="notif-dd-info"><span class="notif-dd-name">${escapeHtml(item.title)}</span><span class="notif-dd-time">${escapeHtml(item.message)}</span></span></a>`;
        }).join("");
    };

    const source = new EventSource(streamUrl, { withCredentials: true });
    source.addEventListener("notifications", (event) => {
        const payload = JSON.parse(event.data);
        updateBadge(payload.unreadCount);
        renderSnapshot().catch(() => {});

        const latest = payload.latest;
        if (initialized && latest && latest.id !== lastLatestId && window.appNotifications) {
            const type = latest.severity === "critical"
                ? "danger"
                : latest.severity === "warning" ? "warning" : "info";
            window.appNotifications.show(`${latest.title}: ${latest.message}`, type);
        }
        lastLatestId = latest?.id ?? null;
        initialized = true;
    });
});
