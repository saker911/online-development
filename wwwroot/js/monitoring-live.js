(function () {
    var refreshIntervalMs = 8000;

    function getDashboardRoot() {
        return document.querySelector("[data-monitoring-dashboard='true']");
    }

    function getStatusChip() {
        return document.querySelector("[data-monitoring-status]");
    }

    function setStatus(state, text) {
        var statusChip = getStatusChip();
        if (!statusChip) {
            return;
        }

        statusChip.setAttribute("data-state", state);
        var textNode = statusChip.querySelector(".monitor-status-text");
        if (textNode) {
            textNode.textContent = text;
        }
    }

    function init() {
        var root = getDashboardRoot();
        if (!root) {
            return;
        }

        var snapshotUrl = root.getAttribute("data-monitoring-snapshot-url") || "";
        if (!snapshotUrl) {
            return;
        }

        var currentRange = root.getAttribute("data-monitoring-range") || "today";
        var refreshQueued = false;
        var refreshInFlight = null;

        function refreshDashboard() {
            if (refreshInFlight) {
                refreshQueued = true;
                return refreshInFlight;
            }

            setStatus("refreshing", "جارٍ التحديث");
            refreshInFlight = fetch(snapshotUrl + "?range=" + encodeURIComponent(currentRange), {
                credentials: "same-origin",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            })
                .then(function (response) {
                    if (!response.ok) {
                        throw new Error("Failed to refresh monitoring dashboard.");
                    }

                    return response.text();
                })
                .then(function (html) {
                    var currentRoot = getDashboardRoot();
                    if (currentRoot) {
                        currentRoot.outerHTML = html;
                        root = getDashboardRoot() || root;
                    }

                    setStatus("live", "يتم التحديث تلقائيًا");
                })
                .catch(function () {
                    setStatus("offline", "تعذر التحديث");
                })
                .finally(function () {
                    refreshInFlight = null;
                    if (refreshQueued) {
                        refreshQueued = false;
                        refreshDashboard();
                    }
                });

            return refreshInFlight;
        }

        setStatus("live", "يتم التحديث تلقائيًا");
        window.setInterval(refreshDashboard, refreshIntervalMs);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init, { once: true });
    } else {
        init();
    }
})();
