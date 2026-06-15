(function () {
    function closePermitActionHub(hub) {
        if (!hub) {
            return;
        }

        hub.classList.remove("is-open");

        var trigger = hub.querySelector(".permit-action-trigger");
        var popover = hub.querySelector(".permit-action-popover");

        if (trigger) {
            trigger.setAttribute("aria-expanded", "false");
        }

        if (popover) {
            popover.hidden = true;
        }
    }

    function positionPopover(trigger, popover) {
        var rect = trigger.getBoundingClientRect();
        var popH = popover.offsetHeight;
        var popW = popover.offsetWidth;
        var viewH = window.innerHeight;
        var viewW = window.innerWidth;
        var gap = 6;

        var top = rect.bottom + gap;
        if (top + popH > viewH && rect.top - gap - popH > 0) {
            top = rect.top - gap - popH;
        }

        var left = rect.right - popW;
        if (left < 8) {
            left = rect.left;
        }
        if (left + popW > viewW - 8) {
            left = viewW - popW - 8;
        }

        popover.style.top = top + "px";
        popover.style.left = left + "px";
    }

    function init() {
        var permitActionHubs = document.querySelectorAll(".permit-action-hub");

        if (window.bootstrap && window.bootstrap.Tooltip) {
            document.querySelectorAll("[data-bs-toggle='tooltip']").forEach(function (element) {
                if (!window.bootstrap.Tooltip.getInstance(element)) {
                    new window.bootstrap.Tooltip(element, { container: "body", trigger: "hover focus" });
                }
            });
        }

        permitActionHubs.forEach(function (hub) {
            var trigger = hub.querySelector(".permit-action-trigger");
            var popover = hub.querySelector(".permit-action-popover");

            if (!trigger || !popover) {
                return;
            }

            trigger.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();

                var shouldOpen = !hub.classList.contains("is-open");

                permitActionHubs.forEach(function (otherHub) {
                    if (otherHub !== hub) {
                        closePermitActionHub(otherHub);
                    }
                });

                if (shouldOpen) {
                    hub.classList.add("is-open");
                    trigger.setAttribute("aria-expanded", "true");
                    popover.hidden = false;
                    positionPopover(trigger, popover);
                } else {
                    closePermitActionHub(hub);
                }
            });

            popover.addEventListener("click", function (event) {
                event.stopPropagation();
            });
        });

        window.addEventListener("scroll", function () {
            permitActionHubs.forEach(closePermitActionHub);
        }, true);
        window.addEventListener("resize", function () {
            permitActionHubs.forEach(closePermitActionHub);
        });

        document.addEventListener("click", function (event) {
            permitActionHubs.forEach(function (hub) {
                if (!hub.classList.contains("is-open")) {
                    return;
                }

                if (!hub.contains(event.target)) {
                    closePermitActionHub(hub);
                }
            });
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
