(() => {
    const page = document.querySelector("[data-emergency-page]");
    if (!page) return;
    const storageKey = "tasareehEmergencyUpdates";
    const networkState = page.querySelector("[data-emergency-network]");
    const readQueue = () => {
        try { return JSON.parse(localStorage.getItem(storageKey) || "[]"); }
        catch { return []; }
    };
    const writeQueue = queue => localStorage.setItem(storageKey, JSON.stringify(queue));
    const setNetworkState = () => {
        if (!networkState) return;
        const count = readQueue().length;
        networkState.textContent = navigator.onLine
            ? (count ? `${count} تحديث بانتظار المزامنة` : "متصل")
            : "دون اتصال: ستُحفظ التحديثات على هذا الجهاز";
    };
    const sync = async () => {
        if (!navigator.onLine) return;
        const queue = readQueue();
        const remaining = [];
        for (const item of queue) {
            try {
                const response = await fetch(item.action, {
                    method: "POST",
                    credentials: "same-origin",
                    headers: { "X-Requested-With": "XMLHttpRequest", "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8" },
                    body: item.body
                });
                if (!response.ok) remaining.push(item);
            } catch { remaining.push(item); }
        }
        writeQueue(remaining);
        setNetworkState();
        if (!remaining.length && queue.length) window.location.reload();
    };
    page.querySelectorAll("[data-emergency-update]").forEach(form => {
        form.addEventListener("submit", event => {
            if (navigator.onLine) return;
            event.preventDefault();
            const data = new URLSearchParams(new FormData(form));
            const queue = readQueue();
            queue.push({ action: form.action, body: data.toString() });
            writeQueue(queue);
            const member = form.closest("[data-member-id]");
            if (member) member.dataset.status = data.get("status") || "Pending";
            setNetworkState();
        });
    });
    window.addEventListener("online", sync);
    window.addEventListener("offline", setNetworkState);
    setNetworkState();
    sync();
})();
