using System.Globalization;
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
using VehiclePermitSystemWeb.Utilities.Online;
using VehiclePermitSystemWeb.Utilities.Reports;

namespace VehiclePermitSystemWeb.Services.Reports
{
    public class ReportsDocumentService : IReportsDocumentService
    {
        private readonly IUserAdminService _userAdminService;
        private readonly IWebHostEnvironment _environment;
        private readonly ISystemClock _systemClock;
        private readonly IConfiguration _configuration;

        public ReportsDocumentService(
            IUserAdminService userAdminService,
            IWebHostEnvironment environment,
            ISystemClock systemClock,
            IConfiguration configuration
        )
        {
            _userAdminService = userAdminService;
            _environment = environment;
            _systemClock = systemClock;
            _configuration = configuration;
        }

        public ReportDocumentResult BuildVisitsReport(VisitFilterResult filter)
        {
            var administration = _userAdminService.GetAdministrationSettings();
            var logoBytes = ResolveLogoBytes(administration.LogoPath);
            var identityLabel = OnlineEditionSettings.IdentityDisplayLabel(_configuration);

            return BuildPdfResult(
                $"VisitsReport_{filter.FileSuffix}",
                Document
                    .Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4.Landscape());
                            page.Margin(20);
                            page.PageColor(Colors.White);
                            page.DefaultTextStyle(
                                TextStyle.Default.FontFamily("Tajawal").FontSize(8)
                            );

                            page.Content()
                                .ContentFromRightToLeft()
                                .Column(column =>
                                {
                                    column.Spacing(10);
                                    ComposeReportHeader(
                                        column,
                                        administration,
                                        logoBytes,
                                        "تقرير الزيارات",
                                        null,
                                        filter.Summary,
                                        filter.Visits.Count
                                    );

                                    if (filter.Visits.Any())
                                    {
                                        column
                                            .Item()
                                            .Table(table =>
                                            {
                                                table.ColumnsDefinition(columns =>
                                                {
                                                    columns.RelativeColumn(0.75f);
                                                    columns.RelativeColumn(1.35f);
                                                    columns.RelativeColumn(1.1f);
                                                    columns.RelativeColumn(0.95f);
                                                    columns.RelativeColumn(1.05f);
                                                    columns.RelativeColumn(1.1f);
                                                    columns.RelativeColumn(0.75f);
                                                    columns.RelativeColumn(0.75f);
                                                    columns.RelativeColumn(0.85f);
                                                    columns.RelativeColumn(0.75f);
                                                    columns.RelativeColumn(0.75f);
                                                    columns.RelativeColumn(0.75f);
                                                });

                                                table.Header(header =>
                                                {
                                                    AddHeaderCell(header, "رقم الزيارة");
                                                    AddHeaderCell(header, "الزائر الرئيسي");
                                                    AddHeaderCell(header, "المرافقون");
                                                    AddHeaderCell(header, identityLabel);
                                                    AddHeaderCell(header, "الغرض");
                                                    AddHeaderCell(header, "الشخص المُزار");
                                                    AddHeaderCell(header, "الاعتماد");
                                                    AddHeaderCell(header, "الحالة");
                                                    AddHeaderCell(header, "تاريخ الزيارة");
                                                    AddHeaderCell(header, "وقت الزيارة");
                                                    AddHeaderCell(header, "وقت الدخول");
                                                    AddHeaderCell(header, "وقت الخروج");
                                                });

                                                foreach (var item in filter.Visits)
                                                {
                                                    AddBodyCell(table, item.VisitId);
                                                    AddBodyCell(table, item.VisitorName);
                                                    AddBodyCell(table, item.CompanionSummary);
                                                    AddBodyCell(
                                                        table,
                                                        FormatVisitNationalId(item.NationalId)
                                                    );
                                                    AddBodyCell(table, item.Purpose);
                                                    AddBodyCell(table, item.SubjectDisplay);
                                                    AddBodyCell(table, item.ApprovalStatusDisplay);
                                                    AddBodyCell(table, item.StatusDisplay);
                                                    AddBodyCell(
                                                        table,
                                                        FormatNumericDate(item.VisitDate)
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        FormatNumericTime(item.VisitDate)
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        FormatNumericTime(item.EntryTime)
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        FormatNumericTime(item.ExitTime)
                                                    );
                                                }
                                            });
                                    }
                                    else
                                    {
                                        column
                                            .Item()
                                            .Border(1)
                                            .BorderColor(Colors.Grey.Lighten1)
                                            .Padding(12)
                                            .AlignCenter()
                                            .Text("لا توجد زيارات مطابقة للفلاتر المحددة.");
                                    }
                                });

                            ComposePageFooter(page);
                        });
                    })
                    .GeneratePdf()
            );
        }

        public ReportDocumentResult BuildPermitActivityReport(
            string? activityQuery,
            IReadOnlyCollection<PermitActivity> activities,
            IReadOnlyDictionary<string, Permit> permitsLookup
        )
        {
            var administration = _userAdminService.GetAdministrationSettings();
            var logoBytes = ResolveLogoBytes(administration.LogoPath);

            return BuildPdfResult(
                "PermitActivityReport",
                Document
                    .Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4.Landscape());
                            page.Margin(20);
                            page.PageColor(Colors.White);
                            page.DefaultTextStyle(
                                TextStyle.Default.FontFamily("Tajawal").FontSize(9)
                            );

                            page.Content()
                                .ContentFromRightToLeft()
                                .Column(column =>
                                {
                                    column.Spacing(10);
                                    ComposeReportHeader(
                                        column,
                                        administration,
                                        logoBytes,
                                        "تقرير حركة دخول وخروج الموظفين",
                                        string.IsNullOrWhiteSpace(activityQuery)
                                            ? null
                                            : $"البحث: {activityQuery}",
                                        null,
                                        activities.Count
                                    );

                                    if (activities.Any())
                                    {
                                        column
                                            .Item()
                                            .Table(table =>
                                            {
                                                table.ColumnsDefinition(columns =>
                                                {
                                                    columns.RelativeColumn(1.35f);
                                                    columns.RelativeColumn(1.35f);
                                                    columns.RelativeColumn(1.25f);
                                                    columns.RelativeColumn(1.2f);
                                                    columns.RelativeColumn(1.25f);
                                                    columns.RelativeColumn(1.0f);
                                                    columns.RelativeColumn(1.2f);
                                                });

                                                table.Header(header =>
                                                {
                                                    AddHeaderCell(header, "رقم التصريح");
                                                    AddHeaderCell(header, "اسم الموظف");
                                                    AddHeaderCell(header, "الحركة");
                                                    AddHeaderCell(header, "التصنيف");
                                                    AddHeaderCell(header, "المشغل");
                                                    AddHeaderCell(header, "الوقت");
                                                    AddHeaderCell(header, "البوابة / المصدر");
                                                });

                                                foreach (var item in activities)
                                                {
                                                    AddBodyCell(table, item.PermitNumber);
                                                    AddBodyCell(table, item.DriverName);
                                                    AddBodyCell(
                                                        table,
                                                        string.IsNullOrWhiteSpace(item.ActionLabel)
                                                            ? FormatPermitActivityAction(
                                                                item.ActionType
                                                            )
                                                            : item.ActionLabel
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        FormatPermitActivityClassification(
                                                            item.ClassificationStatus
                                                        )
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        FormatPermitActivityOperator(item)
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        FormatClock12Hour(item.OccurredAt)
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        FormatPermitActivitySource(item)
                                                    );
                                                }
                                            });
                                    }
                                    else
                                    {
                                        column
                                            .Item()
                                            .Border(1)
                                            .BorderColor(Colors.Grey.Lighten1)
                                            .Padding(12)
                                            .AlignCenter()
                                            .Text("لا توجد حركات مسجلة للطباعة.");
                                    }
                                });

                            ComposePageFooter(page);
                        });
                    })
                    .GeneratePdf()
            );
        }

        public ReportDocumentResult BuildPendingPermitsReport(IReadOnlyCollection<Permit> permits)
        {
            var administration = _userAdminService.GetAdministrationSettings();
            var logoBytes = ResolveLogoBytes(administration.LogoPath);
            var identityLabel = OnlineEditionSettings.IdentityDisplayLabel(_configuration);

            return BuildPdfResult(
                "PendingPermitsReport",
                Document
                    .Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4.Landscape());
                            page.Margin(20);
                            page.PageColor(Colors.White);
                            page.DefaultTextStyle(
                                TextStyle.Default.FontFamily("Tajawal").FontSize(9)
                            );

                            page.Content()
                                .ContentFromRightToLeft()
                                .Column(column =>
                                {
                                    column.Spacing(10);
                                    ComposeReportHeader(
                                        column,
                                        administration,
                                        logoBytes,
                                        "تقرير طلبات التصاريح غير المعتمدة",
                                        "يعرض الطلبات المعلقة ليتم اعتمادها أو رفضها.",
                                        null,
                                        permits.Count
                                    );

                                    if (permits.Any())
                                    {
                                        column
                                            .Item()
                                            .Table(table =>
                                            {
                                                table.ColumnsDefinition(columns =>
                                                {
                                                    columns.RelativeColumn(1f);
                                                    columns.RelativeColumn(1.5f);
                                                    columns.RelativeColumn(1.3f);
                                                    columns.RelativeColumn(1.4f);
                                                    columns.RelativeColumn(1.2f);
                                                    columns.RelativeColumn(1.3f);
                                                    columns.RelativeColumn(1.5f);
                                                });

                                                table.Header(header =>
                                                {
                                                    AddHeaderCell(header, "رقم التصريح");
                                                    AddHeaderCell(header, "اسم المصرح له");
                                                    AddHeaderCell(header, identityLabel);
                                                    AddHeaderCell(header, "الموقع");
                                                    AddHeaderCell(header, "رقم الجوال");
                                                    AddHeaderCell(header, "رقم اللوحة");
                                                    AddHeaderCell(header, "قرار المدير");
                                                });

                                                foreach (var item in permits)
                                                {
                                                    AddBodyCell(table, item.PermitNumber);
                                                    AddBodyCell(table, item.DriverName);
                                                    AddBodyCell(table, item.NationalId);
                                                    AddBodyCell(table, FormatPermitLocation(item));
                                                    AddBodyCell(table, item.EmployeePhone);
                                                    AddBodyCell(table, item.PlateNumberDisplay);
                                                    AddBodyCell(table, "اعتماد / رفض");
                                                }
                                            });
                                    }
                                    else
                                    {
                                        column
                                            .Item()
                                            .Border(1)
                                            .BorderColor(Colors.Grey.Lighten1)
                                            .Padding(12)
                                            .AlignCenter()
                                            .Text("لا توجد تصاريح غير معتمدة حاليًا.");
                                    }
                                });

                            ComposePageFooter(page);
                        });
                    })
                    .GeneratePdf()
            );
        }

        public ReportDocumentResult BuildPermitDetailedReport(
            Permit permit,
            IReadOnlyCollection<PermitActivity> activities
        )
        {
            var administration = _userAdminService.GetAdministrationSettings();
            var logoBytes = ResolveLogoBytes(administration.LogoPath);
            var permitNotice = BuildPermitUsageNotice(administration);
            var identityLabel = OnlineEditionSettings.IdentityDisplayLabel(_configuration);

            return BuildPdfResult(
                $"PermitDetailedReport_{permit.PermitNumber}",
                Document
                    .Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4);
                            page.Margin(20);
                            page.PageColor(Colors.White);
                            page.DefaultTextStyle(
                                TextStyle.Default.FontFamily("Tajawal").FontSize(11)
                            );

                            page.Content()
                                .ContentFromRightToLeft()
                                .Column(column =>
                                {
                                    column.Spacing(12);
                                    ComposeReportHeader(
                                        column,
                                        administration,
                                        logoBytes,
                                        "تقرير تفصيلي لتصريح موظف",
                                        $"رقم التصريح: {permit.PermitNumber}",
                                        null,
                                        activities.Count
                                    );

                                    column
                                        .Item()
                                        .Border(1)
                                        .BorderColor(Colors.Grey.Lighten2)
                                        .Padding(10)
                                        .Column(details =>
                                        {
                                            details.Spacing(6);
                                            details
                                                .Item()
                                                .Text($"رقم التصريح: {permit.PermitNumber}")
                                                .SemiBold();
                                            details
                                                .Item()
                                                .Text($"اسم المصرح له: {permit.DriverName}");
                                            details.Item().Text($"{identityLabel}: {permit.NationalId}");
                                            details
                                                .Item()
                                                .Text(
                                                    $"{GetPermitLocationLabel(permit)}: {FormatPermitLocation(permit)}"
                                                );
                                            details
                                                .Item()
                                                .Text($"رقم الجوال: {permit.EmployeePhone}");
                                            details
                                                .Item()
                                                .Text($"نوع المركبة: {permit.VehicleType}");
                                            details
                                                .Item()
                                                .Text($"رقم اللوحة: {permit.PlateNumberDisplay}");
                                            details
                                                .Item()
                                                .Text(
                                                    $"الحالة الحالية: {permit.ApprovalStatusDisplay}"
                                                );
                                            details
                                                .Item()
                                                .Text(
                                                    $"الانتهاء: {FormatNumericDateTime(permit.ExpiresAt)}"
                                                );
                                        });

                                    column
                                        .Item()
                                        .Border(1)
                                        .BorderColor(Colors.Amber.Medium)
                                        .Background(Colors.Amber.Lighten5)
                                        .Padding(8)
                                        .Text(permitNotice)
                                        .SemiBold()
                                        .FontSize(10)
                                        .AlignRight();

                                    column
                                        .Item()
                                        .Text("سجل الحركات")
                                        .Bold()
                                        .FontSize(14)
                                        .AlignRight();

                                    if (activities.Any())
                                    {
                                        column
                                            .Item()
                                            .Table(table =>
                                            {
                                                table.ColumnsDefinition(columns =>
                                                {
                                                    columns.RelativeColumn(1.15f);
                                                    columns.RelativeColumn(0.95f);
                                                    columns.RelativeColumn(1.2f);
                                                    columns.RelativeColumn(2.2f);
                                                    columns.RelativeColumn(1.2f);
                                                });

                                                table.Header(header =>
                                                {
                                                    AddHeaderCell(header, "التاريخ");
                                                    AddHeaderCell(header, "الوقت");
                                                    AddHeaderCell(header, "الحركة");
                                                    AddHeaderCell(header, "الرسالة");
                                                    AddHeaderCell(header, "المصدر");
                                                });

                                                foreach (var item in activities)
                                                {
                                                    AddBodyCell(
                                                        table,
                                                        FormatNumericDate(item.OccurredAt)
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        FormatNumericTime(item.OccurredAt)
                                                    );
                                                    AddBodyCell(
                                                        table,
                                                        string.IsNullOrWhiteSpace(item.ActionLabel)
                                                            ? FormatPermitActivityAction(
                                                                item.ActionType
                                                            )
                                                            : item.ActionLabel
                                                    );
                                                    AddBodyCell(table, item.Message);
                                                    AddBodyCell(
                                                        table,
                                                        FormatPermitActivitySource(item)
                                                    );
                                                }
                                            });
                                    }
                                    else
                                    {
                                        column
                                            .Item()
                                            .Border(1)
                                            .BorderColor(Colors.Grey.Lighten1)
                                            .Padding(12)
                                            .AlignCenter()
                                            .Text("لا توجد حركات مسجلة لهذا التصريح.");
                                    }
                                });

                            ComposePageFooter(page);
                        });
                    })
                    .GeneratePdf()
            );
        }

        public ReportDocumentResult BuildPermitsReport(PermitReportResult permitReport)
        {
            var administration = _userAdminService.GetAdministrationSettings();
            var logoBytes = ResolveLogoBytes(administration.LogoPath);
            var identityLabel = OnlineEditionSettings.IdentityDisplayLabel(_configuration);

            return BuildPdfResult(
                $"PermitsReport_{permitReport.PermitTypeFilter}",
                Document
                    .Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4.Landscape());
                            page.Margin(20);
                            page.PageColor(Colors.White);
                            page.DefaultTextStyle(
                                TextStyle.Default.FontFamily("Tajawal").FontSize(9)
                            );

                            page.Content()
                                .ContentFromRightToLeft()
                                .Column(column =>
                                {
                                    column.Spacing(10);
                                    ComposeReportHeader(
                                        column,
                                        administration,
                                        logoBytes,
                                        "تقرير التصاريح",
                                        string.IsNullOrWhiteSpace(permitReport.ReportQuery)
                                            ? $"نوع التقرير: {permitReport.PermitTypeFilterDisplay}"
                                            : $"نوع التقرير: {permitReport.PermitTypeFilterDisplay} | البحث: {permitReport.ReportQuery}",
                                        null,
                                        permitReport.Permits.Count
                                    );

                                    if (permitReport.Permits.Any())
                                    {
                                        column
                                            .Item()
                                            .Table(table =>
                                            {
                                                table.ColumnsDefinition(columns =>
                                                {
                                                    columns.RelativeColumn(0.8f);
                                                    columns.RelativeColumn(1.6f);
                                                    columns.RelativeColumn(1.35f);
                                                    columns.RelativeColumn(1.2f);
                                                    columns.RelativeColumn(1.2f);
                                                    columns.RelativeColumn(1.25f);
                                                    columns.RelativeColumn(1.1f);
                                                });

                                                table.Header(header =>
                                                {
                                                    AddHeaderCell(header, "رقم التصريح");
                                                    AddHeaderCell(header, "اسم المصرح له");
                                                    AddHeaderCell(header, identityLabel);
                                                    AddHeaderCell(header, "الموقع");
                                                    AddHeaderCell(header, "رقم الجوال");
                                                    AddHeaderCell(header, "الحالة");
                                                    AddHeaderCell(header, "اللوحة");
                                                });

                                                foreach (var item in permitReport.Permits)
                                                {
                                                    AddBodyCell(table, item.PermitNumber);
                                                    AddBodyCell(table, item.DriverName);
                                                    AddBodyCell(table, item.NationalId);
                                                    AddBodyCell(table, FormatPermitLocation(item));
                                                    AddBodyCell(table, item.EmployeePhone);
                                                    AddBodyCell(table, item.ApprovalStatusDisplay);
                                                    AddBodyCell(table, item.PlateNumberDisplay);
                                                }
                                            });
                                    }
                                    else
                                    {
                                        column
                                            .Item()
                                            .Border(1)
                                            .BorderColor(Colors.Grey.Lighten1)
                                            .Padding(12)
                                            .AlignCenter()
                                            .Text("لا توجد تصاريح مطابقة لهذا التصنيف.");
                                    }
                                });

                            ComposePageFooter(page);
                        });
                    })
                    .GeneratePdf()
            );
        }

        private ReportDocumentResult BuildPdfResult(string fileNamePrefix, byte[] content)
        {
            return new ReportDocumentResult
            {
                Content = content,
                ContentType = "application/pdf",
                FileName = $"{fileNamePrefix}_{_systemClock.LocalNow:yyyyMMddHHmmss}.pdf",
            };
        }

        private void ComposeReportHeader(
            ColumnDescriptor column,
            AdministrationSettings administration,
            byte[]? logoBytes,
            string title,
            string? subtitle,
            string? summary,
            int totalRecords
        )
        {
            column
                .Item()
                .Row(row =>
                {
                    row.Spacing(12);

                    row.ConstantItem(95)
                        .Height(80)
                        .Border(1)
                        .BorderColor(Colors.Blue.Medium)
                        .Background(Colors.Blue.Lighten5)
                        .AlignMiddle()
                        .AlignCenter()
                        .Element(cell =>
                        {
                            if (logoBytes != null)
                            {
                                cell.Padding(6).Image(logoBytes).FitArea();
                            }
                            else
                            {
                                cell.Text("شعار الإدارة")
                                    .FontSize(12)
                                    .SemiBold()
                                    .FontColor(Colors.Blue.Darken2);
                            }
                        });

                    row.RelativeItem()
                        .Column(info =>
                        {
                            info.Spacing(3);
                            info.Item()
                                .AlignRight()
                                .Text(administration.OrganizationName)
                                .Bold()
                                .FontSize(16)
                                .FontColor(Colors.Blue.Darken2);
                            info.Item()
                                .AlignRight()
                                .Text(administration.DepartmentName)
                                .SemiBold()
                                .FontSize(11);

                            if (!string.IsNullOrWhiteSpace(administration.Phone))
                            {
                                info.Item().AlignRight().Text($"الهاتف: {administration.Phone}");
                            }

                            if (!string.IsNullOrWhiteSpace(administration.Address))
                            {
                                info.Item().AlignRight().Text($"العنوان: {administration.Address}");
                            }
                        });
                });

            column.Item().PaddingTop(2).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            column.Item().Text(title).Bold().FontSize(18).AlignCenter();

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                column.Item().Text(subtitle).SemiBold().AlignRight();
            }

            if (!string.IsNullOrWhiteSpace(summary))
            {
                column.Item().Text(summary).AlignRight();
            }

            column
                .Item()
                .Text($"تاريخ الطباعة: {FormatNumericDate(_systemClock.LocalNow)}")
                .AlignRight();
            column
                .Item()
                .Text($"وقت الطباعة: {FormatNumericTime(_systemClock.LocalNow)}")
                .AlignRight();
            column.Item().Text($"إجمالي السجلات: {totalRecords}").AlignRight();
        }

        private static void ComposePageFooter(PageDescriptor page)
        {
            page.Footer()
                .AlignCenter()
                .Text(text =>
                {
                    text.Span("صفحة ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
        }

        private static string GetPermitLocationLabel(Permit permit) =>
            permit.IsVisitorPermit ? "مكان الزيارة" : "موقع العمل";

        private static string FormatPermitLocation(Permit permit)
        {
            var department = (permit.DepartmentName ?? string.Empty).Trim();
            var location = (permit.LocationDisplay ?? string.Empty).Trim();
            if (permit.IsVisitorPermit && !string.IsNullOrWhiteSpace(location))
            {
                return location;
            }

            if (!string.IsNullOrWhiteSpace(department))
            {
                return department;
            }

            return string.IsNullOrWhiteSpace(location) ? "-" : location;
        }

        private byte[]? ResolveLogoBytes(string? logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath))
            {
                return null;
            }

            var normalizedPath = logoPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart('~', '/', '\\');

            var candidatePaths = Path.IsPathRooted(logoPath)
                ? new[] { logoPath }
                : new[]
                {
                    AppStoragePaths.ResolveUploadPhysicalPath(logoPath),
                    Path.Combine(_environment.WebRootPath, normalizedPath),
                    Path.Combine(_environment.ContentRootPath, normalizedPath),
                    Path.Combine(_environment.ContentRootPath, logoPath),
                };

            var resolvedPath = candidatePaths.FirstOrDefault(System.IO.File.Exists);
            return !string.IsNullOrWhiteSpace(resolvedPath)
                ? System.IO.File.ReadAllBytes(resolvedPath)
                : null;
        }

        private static string FormatNumericDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                : "-";
        }

        private static string BuildPermitUsageNotice(AdministrationSettings administration)
        {
            var departmentName = string.IsNullOrWhiteSpace(administration.DepartmentName)
                ? administration.OrganizationName
                : administration.DepartmentName;

            return $"هذا التصريح مخصص للدخول إلى الموقع التابع لـ {departmentName}، ولا يستخدم في أي جهة أخرى. ويتحمل من يبرز هذا التصريح أو يستخدمه خارج الغرض المصرح به المسؤولية الكاملة.";
        }

        private static string FormatNumericTime(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("hh:mm tt", new CultureInfo("ar-SA"))
                : "-";
        }

        private static string FormatNumericDateTime(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy/MM/dd hh:mm tt", new CultureInfo("ar-SA"))
                : "-";
        }

        private static string FormatVisitNationalId(string? value)
        {
            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                return "-";
            }

            return digits.Length <= 10 ? digits : digits[..10];
        }

        private static void AddHeaderCell(TableCellDescriptor header, string text)
        {
            header
                .Cell()
                .Background(Colors.Grey.Lighten3)
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .Padding(4)
                .AlignCenter()
                .Text(text)
                .SemiBold()
                .FontSize(8);
        }

        private static void AddBodyCell(TableDescriptor table, string? text)
        {
            table
                .Cell()
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Padding(4)
                .AlignCenter()
                .AlignMiddle()
                .Text(string.IsNullOrWhiteSpace(text) ? "-" : text)
                .FontSize(8);
        }

        private static string FormatClock12Hour(DateTime? value)
        {
            if (!value.HasValue)
            {
                return "-";
            }

            return value.Value.ToString("hh:mm tt", new CultureInfo("ar-SA"));
        }

        private static string FormatPermitActivityClassification(string? status)
        {
            return PermitActivityDisplayFormatter.Classification(status);
        }

        private static string FormatPermitActivityOperator(PermitActivity item)
        {
            if (!string.IsNullOrWhiteSpace(item.GateOperatorName))
            {
                return string.IsNullOrWhiteSpace(item.GateOperatorAccount)
                    ? item.GateOperatorName
                    : $"{item.GateOperatorName} ({item.GateOperatorAccount})";
            }

            return string.IsNullOrWhiteSpace(item.RecordedBy) ? "-" : item.RecordedBy;
        }

        private static string FormatPermitActivitySource(PermitActivity item)
        {
            return PermitActivityDisplayFormatter.Source(item.GateName, item.Source);
        }

        private static string FormatPermitActivityAction(string? actionType)
        {
            return PermitActivityDisplayFormatter.Action(actionType);
        }
    }
}
