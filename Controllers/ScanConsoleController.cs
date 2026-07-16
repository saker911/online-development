using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Account;
using VehiclePermitSystemWeb.Models.ViewModels.Backup;
using VehiclePermitSystemWeb.Models.ViewModels.Delegations;
using VehiclePermitSystemWeb.Models.ViewModels.Departments;
using VehiclePermitSystemWeb.Models.ViewModels.Display;
using VehiclePermitSystemWeb.Models.ViewModels.Permits;
using VehiclePermitSystemWeb.Models.ViewModels.Reports;
using VehiclePermitSystemWeb.Models.ViewModels.Scan;
using VehiclePermitSystemWeb.Models.ViewModels.Users;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ScanOperations)]
    public class ScanConsoleController : Controller
    {
        private readonly IPermitService _permitService;
        private readonly IVisitService _visitService;

        public ScanConsoleController(IPermitService permitService, IVisitService visitService)
        {
            _permitService = permitService;
            _visitService = visitService;
        }

        [HttpGet]
        public IActionResult Index(string? id = null)
        {
            return View(BuildViewModel(id: id));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(ScanTestViewModel model)
        {
            PopulateSamplePermit(model);
            model.Identifier = (model.Identifier ?? string.Empty).Trim();
            model.ScannerUserName = User.Identity?.Name ?? string.Empty;
            model.ScannedPermit = null;

            if (string.IsNullOrWhiteSpace(model.Identifier))
            {
                ModelState.AddModelError(
                    nameof(model.Identifier),
                    "أدخل القيمة المقروءة من جهاز الباركود."
                );
                return View(model);
            }

            if (
                TryResolvePermitCodeFromVerificationUrl(
                    model.Identifier,
                    out var resolvedPermitCode
                )
            )
            {
                model.Identifier = resolvedPermitCode;
            }

            var identifier = NormalizeIdentifier(model.Identifier, out var detectedMode);
            model.Mode = detectedMode;

            var result =
                detectedMode == ScanTestViewModel.ModePermit
                    ? _permitService.RecordPermitScan(
                        identifier,
                        model.ScannerUserName,
                        false,
                        BuildPermitAuditContext("وحدة المسح اليدوي", "manual")
                    )
                    : _visitService.RecordVisitScan(identifier, model.ScannerUserName);

            model.ScannedPermit =
                detectedMode == ScanTestViewModel.ModePermit
                    ? _permitService.GetPermitByNumber(identifier)
                    : null;

            var scannedName = GetScannedDisplayName(identifier, detectedMode);

            model.Identifier = string.Empty;
            model.LastIdentifier = identifier;
            model.ResultTitle = result.allowed ? "تمت القراءة بنجاح" : "تعذر تنفيذ القراءة";
            model.ResultMessage = TranslateReason(result.reason, detectedMode, scannedName);
            model.ResultClass = result.allowed ? "alert-success" : "alert-danger";
            if (result.allowed)
            {
                this.ToastSuccess(model.ResultMessage);
            }
            else
            {
                this.ToastError(model.ResultMessage);
            }
            return View(model);
        }

        private ScanTestViewModel BuildViewModel(ScanTestViewModel? model = null, string? id = null)
        {
            var viewModel = model ?? new ScanTestViewModel();
            viewModel.ScannerUserName = User.Identity?.Name ?? string.Empty;
            PopulateSamplePermit(viewModel, id);
            return viewModel;
        }

        private void PopulateSamplePermit(ScanTestViewModel model, string? id = null)
        {
            model.IsSpecificPermitBarcode = false;
            model.SamplePermitNotice = string.Empty;

            Permit? samplePermit = null;
            if (!string.IsNullOrWhiteSpace(id))
            {
                var requestedPermit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
                if (requestedPermit == null)
                {
                    model.SamplePermitNotice =
                        "تعذر العثور على التصريح المطلوب أو لا تملك صلاحية عرضه.";
                }
                else if (
                    !string.Equals(
                        requestedPermit.ApprovalStatus,
                        "Approved",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    model.SamplePermitNotice = "لا يمكن عرض باركود البوابة إلا بعد اعتماد التصريح.";
                }
                else
                {
                    samplePermit = requestedPermit;
                    model.IsSpecificPermitBarcode = true;
                }
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                samplePermit = _permitService.GetApprovedPermitsForDisplay().FirstOrDefault();
            }
            model.SamplePermitNumber = samplePermit?.PermitNumber ?? string.Empty;
            model.SamplePermitPublicCode = samplePermit?.PublicPermitCode ?? string.Empty;
            model.SamplePermitDisplayName = samplePermit?.DriverName ?? string.Empty;
        }

        private static string NormalizeIdentifier(string identifier, out string mode)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                mode = ScanTestViewModel.ModePermit;
                return string.Empty;
            }

            var trimmed = identifier.Trim();
            if (trimmed.StartsWith("V", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 1)
            {
                mode = ScanTestViewModel.ModeVisit;
            }
            else if (trimmed.Contains("VISIT", StringComparison.OrdinalIgnoreCase))
            {
                mode = ScanTestViewModel.ModeVisit;
            }
            else
            {
                mode = ScanTestViewModel.ModePermit;
            }

            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var number))
            {
                if (
                    string.Equals(
                        mode,
                        ScanTestViewModel.ModeVisit,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return $"V{number}";
                }

                return $"PERMIT-{number:D5}";
            }

            return trimmed;
        }

        private bool TryResolvePermitCodeFromVerificationUrl(
            string identifier,
            out string permitCode
        )
        {
            permitCode = string.Empty;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            if (!PermitVerificationUrlParser.TryGetToken(identifier, out var token))
            {
                return false;
            }
            _permitService.TryValidatePermitQrToken(
                token,
                out var permit,
                out _,
                out _
            );
            if (permit == null)
            {
                return false;
            }

            permitCode = permit.PublicPermitCode;
            return true;
        }

        private string GetScannedDisplayName(string identifier, string mode)
        {
            if (
                string.Equals(mode, ScanTestViewModel.ModeVisit, StringComparison.OrdinalIgnoreCase)
            )
            {
                return _visitService.GetVisitById(identifier)?.VisitorName ?? string.Empty;
            }

            return _permitService.GetPermitByNumber(identifier)?.DriverName ?? string.Empty;
        }

        private static string TranslateReason(string reason, string mode, string scannedDisplayName)
        {
            var displayName = string.IsNullOrWhiteSpace(scannedDisplayName)
                ? "الشخص"
                : scannedDisplayName;
            var hasResolvedDisplayName = !string.Equals(
                displayName,
                "الشخص",
                StringComparison.Ordinal
            );

            return reason switch
            {
                "Entry recorded" => $"تم دخول {displayName}.",
                "Out recorded" => $"تم خروج {displayName}.",
                "Return recorded" => $"تمت عودة {displayName}.",
                "ReturnAfterUnauthorizedExit recorded" =>
                    $"تمت عودة {displayName} بعد خروج غير مصرح، وتم توثيق الحالة للمراجعة.",
                "LeaveWindowNotStarted" =>
                    "لم تبدأ فترة الاستئذان بعد، ولا يمكن الخروج قبل وقت البداية المحدد.",
                "PendingUnauthorizedExit" =>
                    $"تم رفض خروج {displayName} وسجلت الحالة كمحاولة معلقة للمراجعة.",
                "UnauthorizedExitNeedsReview" =>
                    $"تحولت حالة {displayName} إلى مراجعة إدارية بعد نهاية اليوم.",
                "No return required" =>
                    "تم تسجيل خروج هذا التصريح بدون عودة، ولا يلزم تمريره مرة أخرى.",
                "Permit expired" => "التصريح منتهي ولا يمكن استخدامه.",
                "permit_not_active" => "هذا التصريح غير نشط حاليًا أو ما زال بانتظار الاعتماد.",
                "Permit not found" => hasResolvedDisplayName
                    ? "هذا التصريح موجود لكنه غير نشط حاليًا أو منتهي."
                    : "رقم التصريح غير موجود.",
                "Already returned" => "تمت عودة هذا التصريح مسبقًا.",
                "visit_not_found" => "رقم الزيارة غير موجود.",
                "visit_expired" => "الزيارة منتهية.",
                "visit_not_approved" => "زيارة الموقوف ما زالت بانتظار الاعتماد أو تم رفضها.",
                "entry_recorded" => $"تم دخول {displayName}.",
                "exit_recorded" => $"تم خروج {displayName}.",
                "not_allowed" => mode == ScanTestViewModel.ModeVisit
                    ? "لا يمكن تنفيذ هذه العملية على الزيارة الحالية."
                    : "لا يمكن تنفيذ هذه العملية على التصريح الحالي.",
                _ => string.IsNullOrWhiteSpace(reason)
                    ? "تمت المعالجة بدون تفاصيل إضافية."
                    : reason,
            };
        }

        private PermitScanAuditContext BuildPermitAuditContext(
            string defaultGateName,
            string executionMethod
        )
        {
            var operatorAccount = User.Identity?.Name ?? string.Empty;
            var operatorName =
                User.Claims.FirstOrDefault(claim => claim.Type == "DisplayName")?.Value
                ?? operatorAccount;

            return new PermitScanAuditContext
            {
                GateName = defaultGateName,
                GateOperatorName = operatorName,
                GateOperatorAccount = operatorAccount,
                DeviceId = Request.Headers.UserAgent.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                ExecutionMethod = executionMethod,
                IsAutomated = false,
            };
        }
    }
}
