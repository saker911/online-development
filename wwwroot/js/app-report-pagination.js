document.addEventListener("DOMContentLoaded", function () {
    function ensureReportTableId() {
        if (document.getElementById("report-data-table")) {
            return;
        }

        var tableSection = document.querySelector(".reports-page-shell .report-scroll-table");
        if (!tableSection) {
            return;
        }

        var panel = tableSection.closest(".report-inner-panel");
        if (panel) {
            panel.id = "report-data-table";
        }
    }

    function updatePaginationLinkTargets() {
        document.querySelectorAll(".reports-page-shell .pagination a[href]").forEach(function (link) {
            if (link.href && link.href.indexOf("#") !== -1) {
                link.href = link.href.split("#")[0];
            }
        });
    }

    function resolveReportSection(root) {
        if (!root) {
            return null;
        }

        var directSection = root.getElementById
            ? root.getElementById("report-data-table")
            : null;

        if (directSection) {
            return directSection;
        }

        var fallbackTable = root.querySelector
            ? root.querySelector(".reports-page-shell .report-scroll-table, #report-data-table .report-scroll-table, .report-scroll-table")
            : null;

        if (!fallbackTable) {
            return null;
        }

        var fallbackPanel = fallbackTable.closest(".report-inner-panel");
        if (fallbackPanel) {
            fallbackPanel.id = "report-data-table";
        }

        return fallbackPanel;
    }

    function getStandalonePagination(section) {
        if (!section) {
            return null;
        }

        var sibling = section.nextElementSibling;
        if (sibling && sibling.matches("nav[aria-label]") && sibling.querySelector(".pagination")) {
            return sibling;
        }

        return null;
    }

    function replaceReportSection(doc) {
        var currentSection = resolveReportSection(document);
        var nextSection = resolveReportSection(doc);

        if (!currentSection || !nextSection) {
            return false;
        }

        var currentStandalonePagination = getStandalonePagination(currentSection);
        var nextStandalonePagination = getStandalonePagination(nextSection);

        currentSection.replaceWith(nextSection);

        if (currentStandalonePagination && nextStandalonePagination) {
            currentStandalonePagination.replaceWith(nextStandalonePagination);
        } else if (currentStandalonePagination && !nextStandalonePagination) {
            currentStandalonePagination.remove();
        } else if (!currentStandalonePagination && nextStandalonePagination) {
            nextSection.insertAdjacentElement("afterend", nextStandalonePagination);
        }

        return true;
    }

    async function navigateReportPage(link) {
        var currentSection = document.getElementById("report-data-table");
        if (!currentSection) {
            window.location.href = link.href;
            return;
        }

        var preservedScrollY = window.scrollY;
        var cleanUrl = link.href.split("#")[0];

        currentSection.classList.add("is-loading");

        try {
            var response = await fetch(cleanUrl, {
                credentials: "same-origin",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            if (!response.ok) {
                throw new Error("Failed to fetch report page");
            }

            var html = await response.text();
            var parser = new DOMParser();
            var doc = parser.parseFromString(html, "text/html");

            if (!replaceReportSection(doc)) {
                window.location.href = cleanUrl;
                return;
            }

            window.history.pushState({}, "", cleanUrl);
            ensureReportTableId();
            updatePaginationLinkTargets();
            wireReportPagination();

            if (document.activeElement && typeof document.activeElement.blur === "function") {
                document.activeElement.blur();
            }

            function restoreScrollPosition() {
                window.scrollTo(0, preservedScrollY);
            }

            requestAnimationFrame(function () {
                requestAnimationFrame(restoreScrollPosition);
            });
            setTimeout(restoreScrollPosition, 0);
            setTimeout(restoreScrollPosition, 80);
            setTimeout(restoreScrollPosition, 180);
        } catch (error) {
            window.location.href = cleanUrl;
        }
    }

    function wireReportPagination() {
        document.querySelectorAll(".reports-page-shell .pagination a[href]").forEach(function (link) {
            if (link.dataset.reportPaginationBound === "true") {
                return;
            }

            link.dataset.reportPaginationBound = "true";
            link.addEventListener("click", function (event) {
                if (
                    event.defaultPrevented
                    || event.button !== 0
                    || event.metaKey
                    || event.ctrlKey
                    || event.shiftKey
                    || event.altKey
                ) {
                    return;
                }

                event.preventDefault();
                navigateReportPage(link);
            });
        });
    }

    ensureReportTableId();
    updatePaginationLinkTargets();
    wireReportPagination();
});
