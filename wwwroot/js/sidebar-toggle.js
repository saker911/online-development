(function () {
    function setSidebarOpen(isOpen) {
        var sidebar = document.querySelector("[data-sidebar]");
        var sidebarToggle = document.querySelector("[data-sidebar-toggle]");
        var sidebarBackdrop = document.querySelector("[data-sidebar-close]");

        if (!sidebar || !sidebarToggle || !sidebarBackdrop) {
            return;
        }

        document.body.classList.toggle("app-sidebar-open", isOpen);
        sidebarToggle.setAttribute("aria-expanded", String(isOpen));
        sidebarBackdrop.hidden = !isOpen;
    }

    function init() {
        var sidebar = document.querySelector("[data-sidebar]");
        var sidebarToggle = document.querySelector("[data-sidebar-toggle]");
        var sidebarBackdrop = document.querySelector("[data-sidebar-close]");

        if (!sidebar || !sidebarToggle || !sidebarBackdrop) {
            return;
        }

        sidebarToggle.addEventListener("click", function () {
            setSidebarOpen(!document.body.classList.contains("app-sidebar-open"));
        });

        sidebarBackdrop.addEventListener("click", function () {
            setSidebarOpen(false);
        });

        sidebar.querySelectorAll("a.app-sidebar-link").forEach(function (link) {
            link.addEventListener("click", function () {
                if (window.innerWidth <= 991.98) {
                    setSidebarOpen(false);
                }
            });
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                setSidebarOpen(false);
            }
        });

        window.addEventListener("resize", function () {
            if (window.innerWidth > 991.98) {
                setSidebarOpen(false);
            }
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();