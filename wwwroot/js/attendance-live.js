(() => {
    const page = document.querySelector("[data-attendance-page]");
    const content = page?.querySelector("[data-attendance-content]");
    if (!page || !content) return;

    const refresh = async () => {
        if (document.hidden || page.querySelector("input:focus, select:focus")) return;

        const url = new URL(window.location.href);
        url.pathname = `${url.pathname.replace(/\/$/, "")}/Snapshot`;
        try {
            const response = await fetch(url, {
                headers: { "X-Requested-With": "XMLHttpRequest" },
                credentials: "same-origin",
            });
            if (!response.ok) return;
            content.innerHTML = await response.text();
        } catch {
            // The current snapshot remains visible when connectivity is interrupted.
        }
    };

    window.setInterval(refresh, 30000);
})();
