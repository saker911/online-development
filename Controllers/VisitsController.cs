using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Cryptography;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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
using VehiclePermitSystemWeb.Models.ViewModels.Workplace;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Infrastructure;
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
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;
using VehiclePermitSystemWeb.Services.Workplace;
using VehiclePermitSystemWeb.Utilities.Online;
using VehiclePermitSystemWeb.Utilities.Barcodes;

namespace VehiclePermitSystemWeb.Controllers
{
    public class VisitsController : Controller
    {
        private readonly IVisitService _visitService;
        private readonly IUserAdminService _userAdminService;
        private readonly ISystemClock _systemClock;
        private readonly IAccessControlService _accessControl;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly ITimeLimitedDataProtector _publicVisitProtector;
        private readonly IVisitorWorkflowService? _visitorWorkflowService;
        private readonly IWorkplaceDirectoryService? _workplaceDirectoryService;

        public VisitsController(
            IVisitService visitService,
            IUserAdminService userAdminService,
            ISystemClock systemClock,
            IAccessControlService accessControl,
            IWebHostEnvironment environment,
            IConfiguration configuration,
            IDataProtectionProvider dataProtectionProvider,
            IVisitorWorkflowService? visitorWorkflowService = null,
            IWorkplaceDirectoryService? workplaceDirectoryService = null
        )
        {
            _visitService = visitService;
            _userAdminService = userAdminService;
            _systemClock = systemClock;
            _accessControl = accessControl;
            _environment = environment;
            _configuration = configuration;
            _visitorWorkflowService = visitorWorkflowService;
            _workplaceDirectoryService = workplaceDirectoryService;
            _publicVisitProtector = dataProtectionProvider
                .CreateProtector("VehiclePermitSystem.PublicVisitStatus.v1")
                .ToTimeLimitedDataProtector();
        }

        [AllowAnonymous]
        [HttpGet("/o/{tenant}/visit-request")]
        public IActionResult PublicRequest(string tenant)
        {
            ApplyPublicVisitResponseHeaders();
            if (!TryResolvePublicTenant(tenant, requireAvailableSubscription: true, out var resolvedTenant))
            {
                return NotFound();
            }

            var now = _systemClock.LocalNow;
            var workflow = GetVisitorWorkflowSettings();
            if (!workflow.IsEnabled)
            {
                return NotFound();
            }

            var model = new PublicVisitRequestViewModel
            {
                TenantSlug = resolvedTenant.Slug,
                OrganizationName = ResolvePublicOrganizationName(resolvedTenant),
                VisitDate = now.AddMinutes(Math.Max(30, workflow.MinimumLeadMinutes + 5)),
            };
            ConfigurePublicRequestView(now, model, workflow);
            return View(model);
        }

        [AllowAnonymous]
        [HttpPost("/o/{tenant}/visit-request")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("public-visit-request")]
        public IActionResult PublicRequest(string tenant, PublicVisitRequestViewModel model)
        {
            ApplyPublicVisitResponseHeaders();
            if (!TryResolvePublicTenant(tenant, requireAvailableSubscription: true, out var resolvedTenant))
            {
                return NotFound();
            }

            var now = _systemClock.LocalNow;
            var workflow = GetVisitorWorkflowSettings();
            if (!workflow.IsEnabled)
            {
                return NotFound();
            }

            model.TenantSlug = resolvedTenant.Slug;
            model.OrganizationName = ResolvePublicOrganizationName(resolvedTenant);
            ConfigurePublicRequestView(now, model, workflow);
            NormalizePublicRequestInput(model);
            ModelState.Clear();
            model.Locations = GetPublicVisitLocations();
            if (model.Locations.Count > 0)
            {
                var locationError = string.Empty;
                if (_workplaceDirectoryService == null
                    || !_workplaceDirectoryService.ResolveLocation(
                        model.WorkplaceSiteId,
                        model.WorkplaceSiteEntranceId,
                        "self-service-visits",
                        out var siteName,
                        out var entranceName,
                        out locationError))
                {
                    ModelState.AddModelError(nameof(model.WorkplaceSiteId), locationError);
                }
                else
                {
                    model.VisitLocation = string.IsNullOrWhiteSpace(entranceName)
                        ? siteName
                        : $"{siteName} - {entranceName}";
                }
            }
            TryValidateModel(model);
            ValidatePublicRequestWorkflow(model);
            ValidatePublicRequestSchedule(model, workflow, now);

            if (!string.IsNullOrWhiteSpace(model.Website))
            {
                ModelState.AddModelError(string.Empty, "تعذر إرسال الطلب. حاول مرة أخرى.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var nationalId = string.IsNullOrWhiteSpace(model.NationalId)
                || !model.ShowNationalId
                    ? OnlineEditionSettings.BuildSyntheticNationalId(
                        model.VisitorName,
                        model.PhoneNumber,
                        model.VisitLocation,
                        model.VisitDate.ToString("O")
                    )
                    : model.NationalId;
            var visit = new Visit
            {
                TenantId = resolvedTenant.TenantId,
                VisitorName = model.VisitorName,
                PhoneNumber = model.PhoneNumber,
                VisitorEmail = model.VisitorEmail,
                NationalId = nationalId,
                VisitDate = model.VisitDate,
                VisitedPersonName = string.IsNullOrWhiteSpace(model.VisitedPersonName)
                    ? "الاستقبال"
                    : model.VisitedPersonName,
                HostName = string.IsNullOrWhiteSpace(model.VisitedPersonName)
                    ? "الاستقبال"
                    : model.VisitedPersonName,
                VisitedPersonType = Visit.VisitedPersonTypeHost,
                VisitLocation = string.IsNullOrWhiteSpace(model.VisitLocation)
                    ? "الموقع الرئيسي"
                    : model.VisitLocation,
                WorkplaceSiteId = model.WorkplaceSiteId,
                WorkplaceSiteEntranceId = model.WorkplaceSiteEntranceId,
                Purpose = string.IsNullOrWhiteSpace(model.Purpose)
                    ? "زيارة عامة"
                    : model.Purpose,
                RequestSource = Visit.RequestSourcePublicSelfService,
                RequestedAtUtc = _systemClock.UtcNow,
                Status = "Active",
                ApprovalStatus = "Pending",
            };

            _visitService.AddVisit(visit, "public-visitor");
            var token = _publicVisitProtector.Protect(
                $"{resolvedTenant.TenantId}|{visit.VisitId}",
                TimeSpan.FromDays(30)
            );
            return RedirectToAction(nameof(PublicStatus), new { tenant = resolvedTenant.Slug, token });
        }

        [AllowAnonymous]
        [HttpGet("/o/{tenant}/visit-request/status")]
        public IActionResult PublicStatus(string tenant, string token)
        {
            ApplyPublicVisitResponseHeaders();
            if (!TryResolvePublicTenant(tenant, requireAvailableSubscription: false, out var resolvedTenant)
                || !TryReadPublicVisitToken(token, resolvedTenant.TenantId, out var visitId))
            {
                return NotFound();
            }

            var visit = _visitService.GetVisitById(visitId);
            if (visit == null)
            {
                return NotFound();
            }

            return View(
                new PublicVisitStatusViewModel
                {
                    TenantSlug = resolvedTenant.Slug,
                    OrganizationName = ResolvePublicOrganizationName(resolvedTenant),
                    Token = token,
                    VisitId = visit.VisitId,
                    VisitorName = visit.VisitorName,
                    MaskedPhoneNumber = MaskPhoneNumber(visit.PhoneNumber),
                    VisitLocation = visit.VisitLocation,
                    VisitedPersonName = visit.SubjectDisplay,
                    Purpose = visit.Purpose,
                    VisitDate = visit.VisitDate,
                    ApprovalStatus = visit.ApprovalStatus,
                    Status = visit.Status,
                    QueueTicketNumber = visit.QueueTicketNumber,
                    QueueStatus = visit.QueueStatus,
                    QueueStatusDisplay = visit.QueueStatusDisplay,
                    QueuedAtUtc = visit.QueuedAtUtc,
                    CalledAtUtc = visit.CalledAtUtc,
                    ServiceStartedAtUtc = visit.ServiceStartedAtUtc,
                }
            );
        }

        [AllowAnonymous]
        [HttpGet("/o/{tenant}/visit-request/qr")]
        public IActionResult PublicStatusQr(string tenant, string token)
        {
            ApplyPublicVisitResponseHeaders();
            if (!TryResolvePublicTenant(tenant, requireAvailableSubscription: false, out var resolvedTenant)
                || !TryReadPublicVisitToken(token, resolvedTenant.TenantId, out var visitId))
            {
                return NotFound();
            }

            var visit = _visitService.GetVisitById(visitId);
            if (visit == null
                || !string.Equals(visit.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                || string.Equals(visit.Status, "Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            return Content(BarcodeSvgRenderer.RenderQr(visit.VisitId), "image/svg+xml");
        }

        [Authorize(Policy = AppPolicies.ViewVisits)]
        public IActionResult Index(
            string searchTerm = "",
            string statusFilter = "All",
            int page = 1,
            int pageSize = 10
        )
        {
            return View(BuildIndexViewModel(false, searchTerm, statusFilter, page, pageSize));
        }

        [Authorize(Policy = AppPolicies.ViewVisits)]
        public IActionResult Suspended(
            string searchTerm = "",
            string statusFilter = "Suspended",
            int page = 1,
            int pageSize = 10
        )
        {
            return View(
                "Index",
                BuildIndexViewModel(true, searchTerm, statusFilter, page, pageSize)
            );
        }

        [HttpGet]
        [Authorize(Policy = AppPolicies.ViewVisits)]
        public IActionResult Details(string id)
        {
            var visit = _visitService.GetVisitById(id);
            if (visit == null)
            {
                return NotFound();
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanAccessVisit(visit, currentUser))
            {
                return Forbid();
            }

            ViewData["CanApproveVisit"] =
                string.Equals(visit.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                && _accessControl.CanApproveVisit(visit, currentUser);

            return View(visit);
        }

        [Authorize(Policy = AppPolicies.CreateVisits)]
        public IActionResult Create(long? personId = null)
        {
            ViewData["InitializeCurrentTime"] = true;
            PopulateLocationOptions();
            var visit = new Visit { VisitDate = _systemClock.LocalNow };
            if (personId.HasValue)
            {
                var knownVisitor = _workplaceDirectoryService?.BuildKnownVisitorPrefill(
                    personId.Value
                );
                if (knownVisitor != null)
                {
                    visit.VisitorName = knownVisitor.FullName;
                    visit.NationalId = knownVisitor.NationalId;
                    visit.PhoneNumber = knownVisitor.PhoneNumber;
                    visit.VisitorEmail = knownVisitor.Email;
                    ViewData["KnownVisitorPrefill"] = true;
                }
            }

            return View(visit);
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.CreateVisits)]
        public IActionResult Create(Visit visit)
        {
            NormalizeVisitInput(visit);
            ModelState.Clear();
            ValidateAndApplyLocation(visit, "visits");
            TryValidateModel(visit);
            ValidateVisitSchedule(visit);
            if (ModelState.IsValid)
            {
                try
                {
                    _visitService.AddVisit(visit, User.Identity?.Name);
                    this.ToastSuccess("تم حفظ الزيارة بنجاح.");
                    return RedirectToAction("Index");
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
            }
            PopulateLocationOptions();
            return View(visit);
        }

        [Authorize(Policy = AppPolicies.EditVisits)]
        public IActionResult Edit(string id)
        {
            var visit = _visitService.GetVisitById(id);
            if (visit == null)
            {
                return NotFound();
            }
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanAccessVisit(visit, currentUser))
            {
                return Forbid();
            }
            return View(visit);
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.EditVisits)]
        public IActionResult Edit(Visit visit)
        {
            var existingVisit = _visitService.GetVisitById(visit.VisitId);
            if (existingVisit == null)
            {
                return NotFound();
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanAccessVisit(existingVisit, currentUser))
            {
                return Forbid();
            }

            NormalizeVisitInput(visit);
            ModelState.Clear();
            TryValidateModel(visit);
            ValidateVisitSchedule(visit, existingVisit.VisitDate);
            if (ModelState.IsValid)
            {
                var visitDateChanged = HasVisitDateChanged(existingVisit, visit);
                if (visitDateChanged)
                {
                    visit.Status = "Active";
                }

                var resetApprovalStatus =
                    visitDateChanged || ShouldResetApprovalStatus(existingVisit, visit);
                _visitService.UpdateVisit(visit, User.Identity?.Name, resetApprovalStatus);
                this.ToastSuccess("تم حفظ التعديلات على الزيارة بنجاح.");
                return RedirectToAction("Index");
            }
            return View(visit);
        }

        [Authorize(Policy = AppPolicies.EditVisits)]
        public IActionResult Delete(string id)
        {
            var visit = _visitService.GetVisitById(id);
            if (visit == null)
            {
                return NotFound();
            }
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanAccessVisit(visit, currentUser))
            {
                return Forbid();
            }
            return View(visit);
        }

        [HttpPost, ActionName("Delete")]
        [Authorize(Policy = AppPolicies.EditVisits)]
        public IActionResult DeleteConfirmed(string id)
        {
            var visit = _visitService.GetVisitById(id);
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (visit == null)
            {
                return NotFound();
            }
            if (!_accessControl.CanAccessVisit(visit, currentUser))
            {
                return Forbid();
            }

            _visitService.DeleteVisit(id, User.Identity?.Name);
            this.ToastSuccess("تم حذف الزيارة بنجاح.");
            return RedirectToAction("Index");
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.EditVisits)]
        public IActionResult Suspend(string id)
        {
            var visit = _visitService.GetVisitById(id);
            if (visit == null)
                return NotFound();
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanAccessVisit(visit, currentUser))
                return Forbid();

            _visitService.SuspendVisit(id, User.Identity?.Name);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.EditVisits)]
        public IActionResult Resume(string id)
        {
            var visit = _visitService.GetVisitById(id);
            if (visit == null)
                return NotFound();
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanAccessVisit(visit, currentUser))
                return Forbid();

            _visitService.ResumeVisit(id, User.Identity?.Name);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ApproveDetainedVisits)]
        public IActionResult ApproveDetained(string id)
        {
            var visit = _visitService.GetVisitById(id);
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (visit == null)
            {
                return NotFound();
            }
            if (!_accessControl.CanApproveVisit(visit, currentUser))
            {
                return Forbid();
            }

            var administration = _userAdminService.GetAdministrationSettings();
            if (
                administration == null
                || (
                    administration.SignatureImageData is not { Length: > 0 }
                    && string.IsNullOrWhiteSpace(administration.SignatureImagePath)
                    && string.IsNullOrWhiteSpace(administration.SignatureText)
                )
            )
            {
                this.ToastError(
                    "تعذر اعتماد الزيارة: توقيع المدير غير محفوظ في لوحة الإدارة. الرجاء رفع صورة توقيع في صفحة بيانات الإدارة أولًا."
                );
                return RedirectToAction(nameof(Index));
            }

            _visitService.UpdateVisitApprovalStatus(id, "Approved", User.Identity?.Name);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ApproveDetainedVisits)]
        public IActionResult RejectDetained(string id)
        {
            var visit = _visitService.GetVisitById(id);
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (visit == null)
            {
                return NotFound();
            }
            if (!_accessControl.CanApproveVisit(visit, currentUser))
            {
                return Forbid();
            }

            _visitService.UpdateVisitApprovalStatus(id, "Rejected", User.Identity?.Name);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize(Policy = AppPolicies.ViewVisits)]
        public IActionResult Print(string id)
        {
            var visit = _visitService.GetVisitById(id);
            if (visit == null)
            {
                return NotFound();
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanAccessVisit(visit, currentUser))
            {
                return Forbid();
            }

            if (!CanRenderVisitArtifact(visit))
            {
                return Forbid();
            }

            var administration = _userAdminService.GetAdministrationSettings();
            var logoBytes = ResolveImageBytes(administration.LogoPath);
            var signatureBytes = administration.SignatureImageData is { Length: > 0 }
                ? administration.SignatureImageData
                : ResolveImageBytes(administration.SignatureImagePath);
            var approvalAuthorityDisplay = string.IsNullOrWhiteSpace(administration.DepartmentName)
                ? string.IsNullOrWhiteSpace(administration.OrganizationName)
                    ? "غير محدد"
                    : administration.OrganizationName
                : administration.DepartmentName;
            var managerNameDisplay = string.IsNullOrWhiteSpace(administration.ManagerName)
                ? "غير محدد"
                : administration.ManagerName;
            var signatureTextDisplay = string.IsNullOrWhiteSpace(administration.SignatureText)
                ? "غير محدد"
                : administration.SignatureText;
            var subjectLabelDisplay = "الشخص المُزار";
            var approvalSectionTitle = visit.IsDetainedVisit ? "توقيع المدير" : "اعتماد الإدارة";

            var pdfBytes = Document
                .Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(20);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(TextStyle.Default.FontFamily("Tajawal").FontSize(11));

                        page.Content()
                            .ContentFromRightToLeft()
                            .Column(column =>
                            {
                                column.Spacing(10);

                                column
                                    .Item()
                                    .Row(row =>
                                    {
                                        row.Spacing(12);
                                        row.ConstantItem(110)
                                            .Height(90)
                                            .Border(1)
                                            .BorderColor(Colors.Blue.Medium)
                                            .Background(Colors.Blue.Lighten5)
                                            .AlignMiddle()
                                            .AlignCenter()
                                            .Element(cell =>
                                            {
                                                if (logoBytes != null)
                                                    cell.Padding(8).Image(logoBytes).FitArea();
                                                else
                                                    cell.Text("VP")
                                                        .Bold()
                                                        .FontSize(28)
                                                        .FontColor(Colors.Blue.Darken2);
                                            });

                                        row.RelativeItem()
                                            .Column(info =>
                                            {
                                                info.Spacing(4);
                                                info.Item()
                                                    .AlignRight()
                                                    .Text(administration.OrganizationName)
                                                    .Bold()
                                                    .FontSize(18)
                                                    .FontColor(Colors.Blue.Darken2);
                                                info.Item()
                                                    .AlignRight()
                                                    .Text(administration.DepartmentName)
                                                    .SemiBold()
                                                    .FontSize(12);
                                                info.Item()
                                                    .AlignRight()
                                                    .Text($"العنوان: {administration.Address}");
                                                info.Item()
                                                    .AlignRight()
                                                    .Text($"الهاتف: {administration.Phone}");
                                            });
                                    });

                                column
                                    .Item()
                                    .PaddingTop(4)
                                    .LineHorizontal(1)
                                    .LineColor(Colors.Grey.Lighten1);

                                column
                                    .Item()
                                    .Text("بطاقة زيارة معتمدة")
                                    .Bold()
                                    .FontSize(16)
                                    .AlignCenter();

                                column
                                    .Item()
                                    .Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.RelativeColumn(1.2f);
                                            columns.RelativeColumn(2.5f);
                                        });

                                        AddVisitDetailRow(table, "رقم الزيارة", visit.VisitId);
                                        AddVisitDetailRow(table, "اسم الزائر", visit.VisitorName);
                                        AddVisitDetailRow(table, "رقم الهوية", visit.NationalId);
                                        AddVisitDetailRow(
                                            table,
                                            "مكان الزيارة",
                                            visit.VisitLocation
                                        );
                                        AddVisitDetailRow(
                                            table,
                                            subjectLabelDisplay,
                                            visit.SubjectDisplay
                                        );
                                        AddVisitDetailRow(
                                            table,
                                            "صفة الشخص المُزار",
                                            visit.VisitedPersonTypeDisplay
                                        );
                                        AddVisitDetailRow(table, "الغرض", visit.Purpose);
                                        AddVisitDetailRow(
                                            table,
                                            "عدد المرافقين",
                                            visit.CompanionCount.ToString()
                                        );
                                        AddVisitDetailRow(
                                            table,
                                            "أسماء المرافقين",
                                            visit.CompanionSummary
                                        );
                                        AddVisitDetailRow(
                                            table,
                                            "تاريخ الزيارة",
                                            HijriDateFormatter.Format(visit.VisitDate)
                                        );
                                        AddVisitDetailRow(
                                            table,
                                            "حالة الاعتماد",
                                            visit.ApprovalStatusDisplay
                                        );
                                        AddVisitDetailRow(
                                            table,
                                            "حالة الزيارة",
                                            visit.StatusDisplay
                                        );
                                    });

                                column
                                    .Item()
                                    .Row(row =>
                                    {
                                        row.Spacing(12);

                                        row.RelativeItem()
                                            .Column(signature =>
                                            {
                                                signature.Spacing(4);
                                                signature
                                                    .Item()
                                                    .Border(1)
                                                    .BorderColor(Colors.Grey.Lighten1)
                                                    .Padding(8)
                                                    .Column(block =>
                                                    {
                                                        block.Spacing(3);
                                                        block
                                                            .Item()
                                                            .Text(approvalSectionTitle)
                                                            .Bold();
                                                        block
                                                            .Item()
                                                            .Text(
                                                                $"الإدارة: {approvalAuthorityDisplay}"
                                                            );
                                                        block
                                                            .Item()
                                                            .Text($"المدير: {managerNameDisplay}");
                                                        block
                                                            .Item()
                                                            .Text(
                                                                $"التوقيع الإلكتروني: {signatureTextDisplay}"
                                                            );
                                                        if (signatureBytes != null)
                                                        {
                                                            block
                                                                .Item()
                                                                .PaddingTop(4)
                                                                .Height(55)
                                                                .Image(signatureBytes)
                                                                .FitHeight();
                                                        }
                                                        else
                                                        {
                                                            block
                                                                .Item()
                                                                .Text("لا توجد صورة توقيع مرفقة.")
                                                                .FontSize(9)
                                                                .FontColor(Colors.Grey.Darken1);
                                                        }
                                                        block
                                                            .Item()
                                                            .Text(
                                                                "هذا المستند صادر إلكترونيا ومعتمد للاستخدام الرسمي"
                                                            )
                                                            .FontSize(9)
                                                            .FontColor(Colors.Grey.Darken1);
                                                    });
                                            });

                                        row.ConstantItem(170)
                                            .Column(code =>
                                            {
                                                code.Spacing(6);
                                                code.Item()
                                                    .Border(1)
                                                    .BorderColor(Colors.Grey.Lighten1)
                                                    .Padding(8)
                                                    .AlignCenter()
                                                    .Text(
                                                        $"الزيارة معتمدة: {visit.ApprovalStatusDisplay}"
                                                    )
                                                    .SemiBold();
                                            });
                                    });
                            });
                    });
                })
                .GeneratePdf();

            return File(pdfBytes, "application/pdf", $"Visit_{visit.VisitId}.pdf");
        }

        private VisitIndexViewModel BuildIndexViewModel(
            bool suspendedOnly,
            string searchTerm,
            string statusFilter,
            int page,
            int pageSize
        )
        {
            var normalizedPageSize = PageSizeNormalizer.NormalizePageSize(pageSize);
            IEnumerable<Visit> visits = suspendedOnly
                ? _visitService.GetSuspendedVisits()
                : _visitService.GetAllVisits();

            visits = visits.OrderByDescending(v => ParseVisitNumber(v.VisitId));

            var normalizedStatusFilter = suspendedOnly
                ? "Suspended"
                : NormalizeStatusFilter(statusFilter);

            if (!string.Equals(normalizedStatusFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                visits = visits.Where(v =>
                    string.Equals(
                        v.Status,
                        normalizedStatusFilter,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();
                visits = visits.Where(v =>
                    v.VisitId.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || v.VisitorName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || v.NationalId.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || v.PhoneNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || v.VisitLocation.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || v.Purpose.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || v.SubjectDisplay.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || v.CompanionSummary.Contains(term, StringComparison.OrdinalIgnoreCase)
                );
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            visits = visits.Where(v => _accessControl.CanAccessVisit(v, currentUser));

            var pendingApprovalCount = visits.Count(v =>
                string.Equals(v.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                && _accessControl.CanApproveVisit(v, currentUser)
            );

            var totalCount = visits.Count();
            var totalPages = Math.Max(
                1,
                (int)Math.Ceiling(totalCount / (double)normalizedPageSize)
            );
            var currentPage = Math.Min(Math.Max(page, 1), totalPages);
            var pageVisits = visits
                .Skip((currentPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();
            var approvableVisitIds = pageVisits
                .Where(v =>
                    string.Equals(v.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                    && _accessControl.CanApproveVisit(v, currentUser)
                )
                .Select(v => v.VisitId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentTenant = currentUser == null
                ? null
                : _userAdminService
                    .GetTenants(includeInactive: true)
                    .FirstOrDefault(item => string.Equals(
                        item.TenantId,
                        currentUser.TenantId,
                        StringComparison.OrdinalIgnoreCase
                    ));
            var publicRequestUrl = currentTenant == null
                || !GetVisitorWorkflowSettings().IsEnabled
                || SubscriptionAccessMiddleware.IsSubscriptionRestricted(currentTenant, DateTime.UtcNow)
                    ? string.Empty
                    : Url.Action(
                        nameof(PublicRequest),
                        "Visits",
                        new { tenant = currentTenant.Slug },
                        Request.Scheme
                    ) ?? string.Empty;

            return new VisitIndexViewModel
            {
                SuspendedOnly = suspendedOnly,
                SearchTerm = searchTerm,
                StatusFilter = normalizedStatusFilter,
                Visits = pageVisits,
                ApprovableVisitIds = approvableVisitIds,
                PendingApprovalCount = pendingApprovalCount,
                CurrentPage = currentPage,
                TotalPages = totalPages,
                PageSize = normalizedPageSize,
                PublicRequestUrl = publicRequestUrl,
            };
        }

        private bool TryResolvePublicTenant(
            string? tenantReference,
            bool requireAvailableSubscription,
            out Tenant tenant
        )
        {
            tenant = _userAdminService
                .GetTenants(includeInactive: true)
                .FirstOrDefault(item =>
                    string.Equals(item.TenantId, tenantReference, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Slug, tenantReference, StringComparison.OrdinalIgnoreCase)
                )!;
            if (tenant == null || !tenant.IsActive)
            {
                return false;
            }

            if (requireAvailableSubscription
                && SubscriptionAccessMiddleware.IsSubscriptionRestricted(tenant, DateTime.UtcNow))
            {
                return false;
            }

            HttpContext.Items[HttpTenantContext.ResolvedTenantItemKey] = tenant.TenantId;
            return true;
        }

        private string ResolvePublicOrganizationName(Tenant tenant)
        {
            var administration = _userAdminService.GetAdministrationSettings();
            return string.IsNullOrWhiteSpace(administration.OrganizationName)
                ? tenant.Name
                : administration.OrganizationName.Trim();
        }

        private void ConfigurePublicRequestView(
            DateTime now,
            PublicVisitRequestViewModel model,
            VisitorWorkflowSettingsViewModel workflow
        )
        {
            var sensitiveIdentityHidden = OnlineEditionSettings.HideSensitiveIdentityFields(
                _configuration
            );
            model.ShowNationalId = workflow.ShowNationalId && !sensitiveIdentityHidden;
            model.RequireNationalId = model.ShowNationalId && workflow.RequireNationalId;
            model.ShowHostName = workflow.ShowHostName;
            model.RequireHostName = workflow.ShowHostName && workflow.RequireHostName;
            model.ShowVisitLocation = workflow.ShowVisitLocation;
            model.RequireVisitLocation = workflow.ShowVisitLocation
                && workflow.RequireVisitLocation;
            model.ShowPurpose = workflow.ShowPurpose;
            model.RequirePurpose = workflow.ShowPurpose && workflow.RequirePurpose;
            model.WelcomeMessage = workflow.WelcomeMessage;
            model.Locations = GetPublicVisitLocations();
            ViewData["VisitDateMin"] = now
                .AddMinutes(workflow.MinimumLeadMinutes)
                .ToString("yyyy-MM-ddTHH:mm");
            ViewData["VisitDateMax"] = now
                .AddDays(workflow.MaximumAdvanceDays)
                .ToString("yyyy-MM-ddTHH:mm");
        }

        private IReadOnlyList<WorkplaceLocationOptionViewModel> GetPublicVisitLocations() =>
            _workplaceDirectoryService?.GetLocationOptions()
                .Where(item => item.VisitsEnabled && item.SelfServiceEnabled)
                .ToList() ?? new List<WorkplaceLocationOptionViewModel>();

        private static void NormalizePublicRequestInput(PublicVisitRequestViewModel model)
        {
            model.VisitorName = (model.VisitorName ?? string.Empty).Trim();
            model.PhoneNumber = new string((model.PhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            model.VisitorEmail = (model.VisitorEmail ?? string.Empty).Trim().ToLowerInvariant();
            model.NationalId = new string((model.NationalId ?? string.Empty).Where(char.IsDigit).ToArray());
            model.VisitedPersonName = (model.VisitedPersonName ?? string.Empty).Trim();
            model.VisitLocation = (model.VisitLocation ?? string.Empty).Trim();
            model.Purpose = (model.Purpose ?? string.Empty).Trim();
        }

        private void ValidatePublicRequestWorkflow(PublicVisitRequestViewModel model)
        {
            if (model.ShowNationalId && model.RequireNationalId
                && string.IsNullOrWhiteSpace(model.NationalId))
            {
                ModelState.AddModelError(nameof(model.NationalId), "يرجى إدخال رقم الهوية أو الإقامة.");
            }

            if (model.ShowHostName && model.RequireHostName
                && string.IsNullOrWhiteSpace(model.VisitedPersonName))
            {
                ModelState.AddModelError(nameof(model.VisitedPersonName), "يرجى إدخال اسم الشخص المراد زيارته.");
            }

            if (model.ShowVisitLocation && model.RequireVisitLocation
                && string.IsNullOrWhiteSpace(model.VisitLocation))
            {
                ModelState.AddModelError(nameof(model.VisitLocation), "يرجى إدخال مكان الزيارة.");
            }

            if (model.ShowPurpose && model.RequirePurpose
                && string.IsNullOrWhiteSpace(model.Purpose))
            {
                ModelState.AddModelError(nameof(model.Purpose), "يرجى توضيح سبب الزيارة.");
            }
        }

        private void ValidatePublicRequestSchedule(
            PublicVisitRequestViewModel model,
            VisitorWorkflowSettingsViewModel workflow,
            DateTime now
        )
        {
            if (TruncateToMinute(model.VisitDate)
                < TruncateToMinute(now.AddMinutes(workflow.MinimumLeadMinutes)))
            {
                ModelState.AddModelError(
                    nameof(model.VisitDate),
                    workflow.MinimumLeadMinutes == 0
                        ? "اختر موعدًا صحيحًا يبدأ من الوقت الحالي."
                        : $"اختر موعدًا بعد الوقت الحالي بـ {workflow.MinimumLeadMinutes} دقيقة على الأقل."
                );
            }

            if (model.VisitDate > now.AddDays(workflow.MaximumAdvanceDays))
            {
                ModelState.AddModelError(
                    nameof(model.VisitDate),
                    $"يمكن طلب موعد خلال {workflow.MaximumAdvanceDays} يومًا القادمة فقط."
                );
            }
        }

        private VisitorWorkflowSettingsViewModel GetVisitorWorkflowSettings() =>
            _visitorWorkflowService?.GetSettings()
            ?? VisitorWorkflowSettingsViewModel.CreateDefault();

        private bool TryReadPublicVisitToken(string? token, string tenantId, out string visitId)
        {
            visitId = string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                var payload = _publicVisitProtector.Unprotect(token);
                var parts = payload.Split('|', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2
                    || !string.Equals(parts[0], tenantId, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(parts[1]))
                {
                    return false;
                }

                visitId = parts[1];
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private void ApplyPublicVisitResponseHeaders()
        {
            Response.Headers.CacheControl = "no-store, private";
            Response.Headers.Pragma = "no-cache";
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            Response.Headers["Referrer-Policy"] = "no-referrer";
        }

        private static string MaskPhoneNumber(string phoneNumber)
        {
            var digits = new string((phoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits.Length < 4 ? "••••" : $"••••••{digits[^4..]}";
        }

        private static string NormalizeStatusFilter(string? statusFilter)
        {
            return statusFilter switch
            {
                "Active" => "Active",
                "Inside" => "Inside",
                "Completed" => "Completed",
                "Suspended" => "Suspended",
                _ => "All",
            };
        }

        private static int ParseVisitNumber(string visitId)
        {
            return int.TryParse(visitId.TrimStart('V'), out var value) ? value : 0;
        }

        private void NormalizeVisitInput(Visit visit)
        {
            visit.VisitorName = (visit.VisitorName ?? string.Empty).Trim();
            visit.VisitLocation = (visit.VisitLocation ?? string.Empty).Trim();
            visit.PhoneNumber = new string(
                (visit.PhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray()
            );
            var visitorEmail = (visit.VisitorEmail ?? string.Empty).Trim().ToLowerInvariant();
            visit.VisitorEmail = string.IsNullOrWhiteSpace(visitorEmail) ? null : visitorEmail;
            visit.NationalId = OnlineEditionSettings.HideSensitiveIdentityFields(_configuration)
                ? OnlineEditionSettings.BuildSyntheticNationalId(
                    visit.VisitorName,
                    visit.PhoneNumber,
                    visit.VisitLocation,
                    visit.VisitDate.ToString("O")
                )
                : new string((visit.NationalId ?? string.Empty).Where(char.IsDigit).ToArray());
            visit.Purpose = (visit.Purpose ?? string.Empty).Trim();
            visit.VisitedPersonName = (visit.VisitedPersonName ?? string.Empty).Trim();
            visit.HostName = visit.VisitedPersonName;
            visit.Companions ??= new List<VisitCompanion>();

            foreach (var companion in visit.Companions)
            {
                companion.FullName = (companion.FullName ?? string.Empty).Trim();
                companion.PhoneNumber = new string(
                    (companion.PhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray()
                );
                companion.NationalId = OnlineEditionSettings.HideSensitiveIdentityFields(_configuration)
                    ? string.Empty
                    : new string(
                        (companion.NationalId ?? string.Empty).Where(char.IsDigit).ToArray()
                    );
                companion.Relationship = (companion.Relationship ?? string.Empty).Trim();
            }
        }

        private void PopulateLocationOptions()
        {
            ViewData["WorkplaceLocations"] = _workplaceDirectoryService?.GetLocationOptions()
                .Where(item => item.VisitsEnabled)
                .ToList() ?? new List<WorkplaceLocationOptionViewModel>();
        }

        private void ValidateAndApplyLocation(Visit visit, string service)
        {
            var options = _workplaceDirectoryService?.GetLocationOptions()
                .Where(item => item.VisitsEnabled)
                .ToList() ?? new List<WorkplaceLocationOptionViewModel>();
            if (options.Count == 0 && !visit.WorkplaceSiteId.HasValue)
            {
                return;
            }

            var error = string.Empty;
            if (_workplaceDirectoryService == null
                || !_workplaceDirectoryService.ResolveLocation(
                    visit.WorkplaceSiteId,
                    visit.WorkplaceSiteEntranceId,
                    service,
                    out var siteName,
                    out var entranceName,
                    out error))
            {
                ModelState.AddModelError(
                    nameof(visit.WorkplaceSiteId),
                    string.IsNullOrWhiteSpace(error) ? "تعذر التحقق من الموقع." : error
                );
                return;
            }

            visit.VisitLocation = string.IsNullOrWhiteSpace(entranceName)
                ? siteName
                : $"{siteName} - {entranceName}";
        }

        private static bool ShouldResetApprovalStatus(Visit existingVisit, Visit updatedVisit)
        {
            if (
                string.Equals(existingVisit.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            var isFinalApprovalState =
                string.Equals(
                    existingVisit.ApprovalStatus,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    existingVisit.ApprovalStatus,
                    "Rejected",
                    StringComparison.OrdinalIgnoreCase
                );

            if (!isFinalApprovalState)
            {
                return false;
            }

            return HasMaterialVisitChanges(existingVisit, updatedVisit);
        }

        private static bool HasVisitDateChanged(Visit existingVisit, Visit updatedVisit)
        {
            return TruncateToMinute(existingVisit.VisitDate)
                != TruncateToMinute(updatedVisit.VisitDate);
        }

        private static bool HasMaterialVisitChanges(Visit existingVisit, Visit updatedVisit)
        {
            return !string.Equals(
                    existingVisit.VisitorName,
                    updatedVisit.VisitorName,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    existingVisit.VisitLocation,
                    updatedVisit.VisitLocation,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    existingVisit.NationalId,
                    updatedVisit.NationalId,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    existingVisit.PhoneNumber,
                    updatedVisit.PhoneNumber,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    existingVisit.Purpose,
                    updatedVisit.Purpose,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    existingVisit.SubjectDisplay,
                    updatedVisit.VisitedPersonName,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    existingVisit.VisitedPersonType,
                    updatedVisit.VisitedPersonType,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    existingVisit.CompanionSummary,
                    string.Join(
                        ", ",
                        (updatedVisit.Companions ?? new List<VisitCompanion>())
                            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.FullName))
                            .Select(c => c.FullName.Trim())
                    ),
                    StringComparison.Ordinal
                )
                || TruncateToMinute(existingVisit.VisitDate)
                    != TruncateToMinute(updatedVisit.VisitDate)
                || !string.Equals(
                    existingVisit.Status,
                    updatedVisit.Status,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private void ValidateVisitSchedule(Visit visit, DateTime? existingVisitDate = null)
        {
            var now = _systemClock.LocalNow;

            if (!existingVisitDate.HasValue && visit.VisitDate.Date != now.Date)
            {
                ModelState.AddModelError(
                    nameof(visit.VisitDate),
                    "يجب أن يكون موعد الزيارة في تاريخ اليوم نفسه."
                );
            }

            if (TruncateToMinute(visit.VisitDate) < TruncateToMinute(now))
            {
                ModelState.AddModelError(
                    nameof(visit.VisitDate),
                    "يجب أن يكون موعد الزيارة بنفس وقت النظام أو بعده مباشرة."
                );
            }
        }

        private static DateTime TruncateToMinute(DateTime value)
        {
            return new DateTime(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                0,
                value.Kind
            );
        }

        private static bool CanRenderVisitArtifact(Visit visit)
        {
            return string.Equals(
                visit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static void AddVisitDetailRow(TableDescriptor table, string label, string value)
        {
            table
                .Cell()
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .Background(Colors.Grey.Lighten4)
                .PaddingVertical(5)
                .PaddingHorizontal(8)
                .AlignMiddle()
                .Text(label)
                .SemiBold();
            table
                .Cell()
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingVertical(5)
                .PaddingHorizontal(8)
                .AlignMiddle()
                .Text(string.IsNullOrWhiteSpace(value) ? "-" : value);
        }

        private byte[]? ResolveImageBytes(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            var normalizedPath = imagePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart('~', '/', '\\');

            var candidatePaths = Path.IsPathRooted(imagePath)
                ? new[] { imagePath }
                : new[]
                {
                    AppStoragePaths.ResolveUploadPhysicalPath(imagePath),
                    Path.Combine(_environment.WebRootPath, normalizedPath),
                    Path.Combine(_environment.ContentRootPath, normalizedPath),
                    Path.Combine(_environment.ContentRootPath, imagePath),
                    Path.Combine(AppContext.BaseDirectory, normalizedPath),
                };

            var resolvedPath = candidatePaths.FirstOrDefault(System.IO.File.Exists);

            return !string.IsNullOrWhiteSpace(resolvedPath)
                ? System.IO.File.ReadAllBytes(resolvedPath)
                : null;
        }
    }
}
