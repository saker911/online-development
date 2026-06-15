namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioOperationalTablesUseHybridIconActions()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var permitsView = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Permits", "Index.cshtml")
        );
        var visitsView = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Visits", "Index.cshtml")
        );
        var styles = File.ReadAllText(
            Path.Combine(repositoryRoot, "wwwroot", "css", "theme-tables.css")
        );
        var script = File.ReadAllText(
            Path.Combine(repositoryRoot, "wwwroot", "js", "permit-action-hubs.js")
        );

        Require(
            permitsView.Contains("record-row-actions", StringComparison.Ordinal)
                && permitsView.Contains("record-icon-action", StringComparison.Ordinal)
                && permitsView.Contains("data-bs-toggle=\"tooltip\"", StringComparison.Ordinal)
                && permitsView.Contains("permit-action-popover", StringComparison.Ordinal),
            "permits table should expose compact icon actions and keep secondary actions in the existing popover"
        );
        Require(
            visitsView.Contains("record-row-actions", StringComparison.Ordinal)
                && visitsView.Contains("record-icon-action", StringComparison.Ordinal)
                && visitsView.Contains("data-bs-toggle=\"tooltip\"", StringComparison.Ordinal)
                && visitsView.Contains("permit-action-popover", StringComparison.Ordinal),
            "visits table should expose compact icon actions and keep secondary actions in the existing popover"
        );
        Require(
            styles.Contains(".record-icon-action", StringComparison.Ordinal)
                && styles.Contains(
                    "[data-theme=\"dark\"] .record-icon-action",
                    StringComparison.Ordinal
                )
                && styles.Contains(
                    ".record-row-actions .permit-action-trigger",
                    StringComparison.Ordinal
                ),
            "operational icon actions should have shared light/dark styling"
        );
        Require(
            script.Contains("bootstrap.Tooltip", StringComparison.Ordinal)
                && script.Contains("[data-bs-toggle='tooltip']", StringComparison.Ordinal),
            "operational action tooltips should be initialized by the shared action script"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioAdministrationTablesUseCompactIconActions()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var delegationsView = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Delegations", "Index.cshtml")
        );
        var departmentsView = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Administration", "Departments.cshtml")
        );
        var displaySettingsView = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Administration", "DisplaySettings.cshtml")
        );
        var displayDevicesView = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Administration", "DisplayDevices.cshtml")
        );

        Require(
            delegationsView.Contains("delegation-row-actions", StringComparison.Ordinal)
                && delegationsView.Contains("record-icon-action", StringComparison.Ordinal)
                && delegationsView.Contains("data-bs-toggle=\"tooltip\"", StringComparison.Ordinal),
            "delegation rows should use compact icon actions with hover labels"
        );
        Require(
            departmentsView.Contains("department-row-actions", StringComparison.Ordinal)
                && departmentsView.Contains("record-icon-action", StringComparison.Ordinal)
                && departmentsView.Contains("data-bs-toggle=\"tooltip\"", StringComparison.Ordinal),
            "department rows should use compact icon actions with hover labels"
        );
        Require(
            displaySettingsView.Contains("display-device-row-actions", StringComparison.Ordinal)
                && displaySettingsView.Contains("record-icon-action", StringComparison.Ordinal)
                && displayDevicesView.Contains(
                    "display-device-row-actions",
                    StringComparison.Ordinal
                )
                && displayDevicesView.Contains("record-icon-action", StringComparison.Ordinal),
            "display device management rows should use compact icon actions in both display pages"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUserEditorLiveSummaryUsesCompactTypography()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var styles = File.ReadAllText(
            Path.Combine(repositoryRoot, "wwwroot", "css", "users-pages.css")
        );

        Require(
            styles.Contains(".ue-wizard-summary .ue-card-title h2", StringComparison.Ordinal)
                && styles.Contains(
                    "font-size: clamp(1rem, 1.35vw, 1.22rem)",
                    StringComparison.Ordinal
                )
                && styles.Contains(
                    ".ue-wizard-summary .ue-summary-item span",
                    StringComparison.Ordinal
                )
                && styles.Contains("font-size: 0.72rem", StringComparison.Ordinal),
            "user editor live summary heading and labels should use compact form typography"
        );
        Require(
            styles.Contains("min-height: 54px", StringComparison.Ordinal)
                && styles.Contains(
                    "font-size: clamp(0.78rem, 0.88vw, 0.9rem)",
                    StringComparison.Ordinal
                ),
            "user editor live summary cards should stay compact with smaller value text"
        );
        Require(
            styles.Contains("grid-template-columns: 1fr", StringComparison.Ordinal)
                && styles.Contains(
                    "repeat(auto-fit, minmax(190px, 1fr))",
                    StringComparison.Ordinal
                ),
            "user editor live summary should fill the available page width instead of staying in a narrow column"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioUserEditorReviewLayoutAvoidsEmptyCardsAndIconOverlap()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var view = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Users", "_UserEditor.cshtml")
        );
        var styles = File.ReadAllText(
            Path.Combine(repositoryRoot, "wwwroot", "css", "users-pages.css")
        );

        Require(
            view.Contains("data-users-panel-container", StringComparison.Ordinal)
                && view.Contains("wizardPanelContainers", StringComparison.Ordinal)
                && view.Contains(
                    "container.classList.toggle(\"is-hidden\", !hasVisiblePanel)",
                    StringComparison.Ordinal
                ),
            "user editor should hide step container cards when all nested wizard panels are hidden"
        );
        Require(
            view.Contains("cleanDepartmentManagerSummary", StringComparison.Ordinal)
                && view.Contains("replace(/^المدير\\s+الحالي", StringComparison.Ordinal),
            "department manager summary should display the manager name without the noisy current-manager prefix"
        );
        Require(
            styles.Contains(
                "grid-template-columns: 18px 28px 78px minmax(0, 1fr) auto",
                StringComparison.Ordinal
            )
                && styles.Contains("gap: 6px", StringComparison.Ordinal)
                && styles.Contains("margin-inline-start: 0", StringComparison.Ordinal),
            "permission group icons should reserve enough space and avoid overlapping"
        );
        Require(
            styles.Contains(".ue-stats-bar", StringComparison.Ordinal)
                && styles.Contains(
                    "repeat(auto-fit, minmax(220px, 1fr))",
                    StringComparison.Ordinal
                ),
            "account review stats should use compact full-width cards"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioPrintBadgesSuppressBrowserUrlHeaderFooter()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var userBadge = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Users", "PrintUserBadge.cshtml")
        );
        var operatorBadge = File.ReadAllText(
            Path.Combine(repositoryRoot, "Views", "Users", "PrintOperatorBadge.cshtml")
        );

        foreach (var view in new[] { userBadge, operatorBadge })
        {
            Require(
                view.Contains("@@page", StringComparison.Ordinal)
                    && view.Contains("margin: 0", StringComparison.Ordinal)
                    && view.Contains("padding: 10mm", StringComparison.Ordinal)
                    && view.Contains("break-inside: avoid", StringComparison.Ordinal),
                "print badge pages should suppress browser URL headers/footers and keep the card inside a safe printable area"
            );
        }

        return Task.CompletedTask;
    }

    private static Task ScenarioGateDisplayClearsOperatorStateAndConstrainsWideDetails()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var gateScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "wwwroot", "js", "gate-display.js")
        );
        var gateStyles = File.ReadAllText(
            Path.Combine(repositoryRoot, "wwwroot", "css", "gate-display.css")
        );

        Require(
            gateScript.Contains("const clearPermitContext", StringComparison.Ordinal)
                && gateScript.Contains("lastScannedPermitNumber = ''", StringComparison.Ordinal)
                && gateScript.Contains("operatorNoteInput.value = ''", StringComparison.Ordinal),
            "gate display should clear previous permit and note context when the operator changes"
        );
        Require(
            gateScript.Contains(
                "operatorBadgeInput.value = changeMode ? operatorBadgeInput.value : (initialBadge || '')",
                StringComparison.Ordinal
            ),
            "operator switch modal should not keep the previous operator badge when opened manually"
        );
        Require(
            gateScript.Contains("if (previousUsername !== currentUsername)", StringComparison.Ordinal)
                && gateScript.Contains(
                    "clearPermitContext('تم تبديل المشغل. امسح التصريح التالي.')",
                    StringComparison.Ordinal
                )
                && gateScript.Contains(
                    "clearPermitContext('تم تسجيل خروج المشغل. سجّل دخول مشغل للمتابعة.')",
                    StringComparison.Ordinal
                ),
            "operator login and sign-out should reset the active permit panel instead of mixing users"
        );
        Require(
            gateScript.Contains("const buildActivitySignature", StringComparison.Ordinal)
                && gateScript.Contains("serverSignatures.has(signature)", StringComparison.Ordinal),
            "gate activity refresh should de-duplicate local scanner activity against server activity"
        );
        Require(
            gateStyles.Contains("#permitDetailsState", StringComparison.Ordinal)
                && gateStyles.Contains("width: min(100%, 1040px)", StringComparison.Ordinal)
                && gateStyles.Contains(
                    "grid-template-columns: repeat(3, minmax(0, 1fr))",
                    StringComparison.Ordinal
                ),
            "wide gate display permit details should stay readable instead of stretching across TV screens"
        );

        return Task.CompletedTask;
    }
}
