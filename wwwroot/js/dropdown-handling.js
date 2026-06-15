(function () {
    function init() {
        var dropdownToggles = document.querySelectorAll("[data-bs-toggle='dropdown']");

        if (window.bootstrap && window.bootstrap.Dropdown) {
            dropdownToggles.forEach(function (toggle) {
                window.bootstrap.Dropdown.getOrCreateInstance(toggle, {
                    autoClose: toggle.getAttribute("data-bs-auto-close") || true,
                });
            });

            document.querySelectorAll(".notification-icons .icon-pill").forEach(function (btn) {
                btn.addEventListener("click", function (event) {
                    document.querySelectorAll(".dropdown-menu.show").forEach(function (openMenu) {
                        var parent = openMenu.parentElement;
                        if (parent && !parent.contains(event.currentTarget)) {
                            var toggle = parent.querySelector("[data-bs-toggle='dropdown']");
                            try {
                                var instance = window.bootstrap.Dropdown.getInstance(toggle);
                                if (instance) {
                                    instance.hide();
                                }
                            } catch (error) {
                            }
                        }
                    });
                });
            });
            return;
        }

        dropdownToggles.forEach(function (toggle) {
            toggle.addEventListener("click", function (event) {
                event.preventDefault();
                var menu = toggle.parentElement.querySelector(".dropdown-menu");
                if (!menu) {
                    return;
                }

                var isShown = menu.classList.contains("show");
                document.querySelectorAll(".dropdown-menu.show").forEach(function (openMenu) {
                    openMenu.classList.remove("show");
                });

                if (!isShown) {
                    menu.classList.add("show");
                }
            });
        });

        document.addEventListener("click", function (event) {
            if (!event.target.closest(".dropdown")) {
                document.querySelectorAll(".dropdown-menu.show").forEach(function (openMenu) {
                    openMenu.classList.remove("show");
                });
            }
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();