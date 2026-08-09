document.addEventListener("DOMContentLoaded", function () {
    const root = document.getElementById("waitingBoardRoot");
    if (!root) {
        return;
    }

    const statusElement = document.getElementById("waitingBoardStatus");
    const clockElement = document.getElementById("waitingBoardClock");
    const groups = {
        waiting: {
            list: document.getElementById("waitingBoardWaitingList"),
            count: document.getElementById("waitingBoardWaitingCount"),
            empty: "لا توجد مواعيد بانتظار الوصول."
        },
        inside: {
            list: document.getElementById("waitingBoardInsideList"),
            count: document.getElementById("waitingBoardInsideCount"),
            empty: "لا توجد زيارات داخل الجهة حاليًا."
        },
        completed: {
            list: document.getElementById("waitingBoardCompletedList"),
            count: document.getElementById("waitingBoardCompletedCount"),
            empty: "لا توجد زيارات مكتملة مؤخرًا."
        }
    };

    function createTicket(ticket) {
        const article = document.createElement("article");
        article.className = "waiting-ticket";

        const number = document.createElement("strong");
        number.className = "ltr-field";
        number.textContent = ticket.ticketNumber || "V-----";

        const details = document.createElement("div");
        const location = document.createElement("span");
        location.textContent = ticket.location || "الاستقبال";
        const time = document.createElement("time");
        time.textContent = ticket.timeText || "-";
        details.append(location, time);
        article.append(number, details);
        return article;
    }

    function renderGroup(key, items) {
        const group = groups[key];
        if (!group?.list) {
            return;
        }

        const safeItems = Array.isArray(items) ? items : [];
        group.list.replaceChildren();
        group.count.textContent = String(safeItems.length);
        if (!safeItems.length) {
            const empty = document.createElement("p");
            empty.className = "waiting-board-empty";
            empty.textContent = group.empty;
            group.list.appendChild(empty);
            return;
        }

        safeItems.forEach((ticket) => group.list.appendChild(createTicket(ticket)));
    }

    async function refreshBoard() {
        try {
            const response = await fetch(root.dataset.statusEndpoint, {
                headers: { "Accept": "application/json" },
                cache: "no-store"
            });
            if (!response.ok) {
                throw new Error("waiting-board-status-failed");
            }

            const payload = await response.json();
            renderGroup("waiting", payload.waiting);
            renderGroup("inside", payload.inside);
            renderGroup("completed", payload.completed);
            clockElement.textContent = payload.updatedAtText || clockElement.textContent;
            statusElement.textContent = "متصل";
            statusElement.classList.remove("is-error");
        } catch {
            statusElement.textContent = "تعذر التحديث";
            statusElement.classList.add("is-error");
        }
    }

    function heartbeat() {
        fetch(root.dataset.heartbeatEndpoint, {
            method: "POST",
            credentials: "same-origin"
        }).catch(function () { });
    }

    refreshBoard();
    heartbeat();
    window.setInterval(refreshBoard, 10000);
    window.setInterval(heartbeat, 30000);
});
