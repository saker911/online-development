using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
using VehiclePermitSystemWeb.Services.Display;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;
using VehiclePermitSystemWeb.Utilities.Online;

namespace VehiclePermitSystemWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = AppPolicies.ScanOperations)]
    public class ScanController : ControllerBase
    {
        private readonly IPermitService _permitService;
        private readonly IVisitService _visitService;
        private readonly IUserAdminService _userAdminService;
        private readonly IDisplayDeviceService _displayDeviceService;
        private readonly IConfiguration _configuration;

        public ScanController(
            IPermitService permitService,
            IVisitService visitService,
            IUserAdminService userAdminService,
            IDisplayDeviceService displayDeviceService,
            IConfiguration? configuration = null
        )
        {
            _permitService = permitService;
            _visitService = visitService;
            _userAdminService = userAdminService;
            _displayDeviceService = displayDeviceService;
            _configuration = configuration ?? new ConfigurationBuilder().Build();
        }

        [HttpPost("permit")]
        [HttpPost("~/api/permits/{permitNumber}/scan")]
        public IActionResult ScanPermit(
            [FromRoute] string? permitNumber,
            [FromBody] JsonElement requestBody
        )
        {
            var rawIdentifier = ResolveIdentifier(permitNumber, requestBody);
            if (TryResolvePermitCodeFromVerificationUrl(rawIdentifier, out var resolvedPermitCode))
            {
                rawIdentifier = resolvedPermitCode;
            }

            var identifier = NormalizePermitIdentifier(rawIdentifier);
            if (string.IsNullOrWhiteSpace(identifier))
                return Ok(new { allowed = false, reason = "invalid_request" });

            var scannerUserId = ResolveEffectiveScannerUserId(requestBody);
            var auditContext = BuildPermitAuditContext(requestBody, "نقطة مسح التصاريح", "scan");
            var overrideEntry = ResolveOverrideEntry(
                requestBody,
                auditContext,
                out var overrideFailureReason
            );
            if (!string.IsNullOrWhiteSpace(overrideFailureReason))
            {
                return Ok(
                    new
                    {
                        allowed = false,
                        reason = overrideFailureReason,
                        overrideEntry = false,
                    }
                );
            }
            var permitBeforeScan = _permitService.GetPermitByNumber(identifier);
            if (permitBeforeScan != null && !CanScanWorkplaceSite(permitBeforeScan.WorkplaceSiteId, scannerUserId))
            {
                return Ok(new { allowed = false, reason = "site_access_denied", identifier });
            }
            var (allowed, reason) = _permitService.RecordPermitScan(
                identifier,
                scannerUserId,
                overrideEntry,
                auditContext
            );
            var permit = _permitService.GetPermitByNumber(identifier) ?? permitBeforeScan;
            return Ok(
                new
                {
                    allowed,
                    reason,
                    overrideEntry,
                    identifier,
                    displayName = permit?.DriverName ?? string.Empty,
                    permit = permit == null
                        ? null
                        : new
                        {
                            permitNumber = permit.PermitNumber,
                            publicPermitCode = permit.PublicPermitCode,
                            driverName = permit.DriverName,
                            permitTypeDisplay = permit.PermitTypeDisplay,
                            approvalStatusDisplay = permit.ApprovalStatusDisplay,
                            departmentName = permit.DepartmentName,
                            locationDisplay = permit.LocationDisplay,
                            subject = permit.Subject,
                            nationalId = MaskIdentity(permit.NationalId),
                            vehicleType = permit.VehicleType,
                            plateNumberDisplay = permit.PlateNumberDisplay,
                            employeePhone = permit.EmployeePhone,
                            permitDateText = permit.PermitDate.HasValue
                                ? permit.PermitDate.ToString()
                                : string.Empty,
                            expiresAtText = permit.ExpiresAt.HasValue
                                ? permit.ExpiresAt.Value.ToString()
                                : string.Empty,
                            approvalStatus = permit.ApprovalStatus,
                            currentState = permit.CurrentState,
                            requiresReturn = permit.RequiresReturn,
                            pendingExitRequest = permit.PendingExitRequest,
                        },
                }
            );
        }

        [HttpPost("auto")]
        [AllowAnonymous]
        [EnableRateLimiting("gate-scan")]
        public IActionResult ScanAuto([FromBody] JsonElement requestBody)
        {
            if (!HasScanAccess())
            {
                return Forbid();
            }

            var rawIdentifier = ResolveIdentifier(null, requestBody);
            if (TryResolvePermitCodeFromVerificationUrl(rawIdentifier, out var resolvedPermitCode))
            {
                rawIdentifier = resolvedPermitCode;
            }

            var (identifier, scanMode) = ResolveAutoIdentifier(rawIdentifier);
            if (string.IsNullOrWhiteSpace(identifier))
                return Ok(new { allowed = false, reason = "invalid_request" });

            var requestedScannerUserId = ResolveEffectiveScannerUserId(requestBody);
            var auditContext = BuildPermitAuditContext(requestBody, "البوابة", "scan");
            var displayOperator = ResolveActiveDisplayOperatorForScan(auditContext.DeviceId);
            if (displayOperator?.MustChangePin == true)
            {
                return Ok(
                    new
                    {
                        allowed = false,
                        reason = "operator_pin_change_required",
                        overrideEntry = false,
                        identifier,
                        scanMode,
                    }
                );
            }

            if (RequiresActiveDisplayOperatorForScan() && displayOperator == null)
            {
                return Ok(
                    new
                    {
                        allowed = false,
                        reason = "operator_not_signed_in",
                        overrideEntry = false,
                        identifier,
                        scanMode,
                    }
                );
            }

            var scannerUserId = displayOperator?.Username ?? requestedScannerUserId;
            var overrideEntry = ResolveOverrideEntry(
                requestBody,
                auditContext,
                out var overrideFailureReason
            );
            if (!string.IsNullOrWhiteSpace(overrideFailureReason))
            {
                return Ok(
                    new
                    {
                        allowed = false,
                        reason = overrideFailureReason,
                        overrideEntry = false,
                    }
                );
            }

            var permitBeforeScan = _permitService.GetPermitByNumber(identifier);

            if (string.Equals(scanMode, "visit", StringComparison.OrdinalIgnoreCase))
            {
                var visitBeforeScan = _visitService.GetVisitById(identifier);
                if (visitBeforeScan != null && !CanScanWorkplaceSite(visitBeforeScan.WorkplaceSiteId, scannerUserId))
                {
                    return Ok(
                        new
                        {
                            allowed = false,
                            reason = "site_access_denied",
                            overrideEntry = false,
                            identifier,
                            scanMode,
                        }
                    );
                }
                var (allowed, reason) = _visitService.RecordVisitScan(identifier, scannerUserId);
                var visit = _visitService.GetVisitById(identifier) ?? visitBeforeScan;
                return Ok(
                    new
                    {
                        allowed,
                        reason,
                        overrideEntry,
                        identifier,
                        scanMode,
                        displayName = visit?.VisitorName ?? string.Empty,
                        visit = visit == null
                            ? null
                            : new
                            {
                                visitId = visit.VisitId,
                                visitorName = visit.VisitorName,
                                companionCount = visit.CompanionCount,
                                companionSummary = visit.CompanionSummary,
                                status = visit.Status,
                                approvalStatus = visit.ApprovalStatus,
                                visitLocation = visit.VisitLocation,
                                hostName = visit.SubjectDisplay,
                                visitedPersonName = visit.SubjectDisplay,
                                visitedPersonType = visit.VisitedPersonTypeDisplay,
                                nationalId = MaskIdentity(visit.NationalId),
                                phoneNumber = MaskSensitiveValue(visit.PhoneNumber, 4),
                            },
                    }
                );
            }

            if (permitBeforeScan != null && !CanScanWorkplaceSite(permitBeforeScan.WorkplaceSiteId, scannerUserId))
            {
                return Ok(
                    new
                    {
                        allowed = false,
                        reason = "site_access_denied",
                        overrideEntry = false,
                        identifier,
                        scanMode,
                    }
                );
            }
            var (permitAllowed, permitReason) = _permitService.RecordPermitScan(
                identifier,
                scannerUserId,
                overrideEntry,
                auditContext
            );
            var permit = _permitService.GetPermitByNumber(identifier) ?? permitBeforeScan;
            return Ok(
                new
                {
                    allowed = permitAllowed,
                    reason = permitReason,
                    overrideEntry,
                    identifier,
                    scanMode,
                    displayName = permit?.DriverName ?? string.Empty,
                    permit = permit == null
                        ? null
                        : new
                        {
                            permitNumber = permit.PermitNumber,
                            publicPermitCode = permit.PublicPermitCode,
                            driverName = permit.DriverName,
                            permitTypeDisplay = permit.PermitTypeDisplay,
                            approvalStatusDisplay = permit.ApprovalStatusDisplay,
                            departmentName = permit.DepartmentName,
                            locationDisplay = permit.LocationDisplay,
                            subject = permit.Subject,
                            nationalId = MaskIdentity(permit.NationalId),
                            vehicleType = permit.VehicleType,
                            plateNumberDisplay = permit.PlateNumberDisplay,
                            employeePhone = MaskSensitiveValue(permit.EmployeePhone, 4),
                            permitDateText = permit.PermitDate.HasValue
                                ? permit.PermitDate.ToString()
                                : string.Empty,
                            expiresAtText = permit.ExpiresAt.HasValue
                                ? permit.ExpiresAt.Value.ToString()
                                : string.Empty,
                            approvalStatus = permit.ApprovalStatus,
                            currentState = permit.CurrentState,
                            requiresReturn = permit.RequiresReturn,
                            pendingExitRequest = permit.PendingExitRequest,
                        },
                }
            );
        }

        [HttpPost("visit")]
        [HttpPost("~/api/visits/{visitId}/scan")]
        public IActionResult ScanVisit(
            [FromRoute] string? visitId,
            [FromBody] JsonElement requestBody
        )
        {
            var identifier = NormalizeVisitIdentifier(ResolveIdentifier(visitId, requestBody));
            if (string.IsNullOrWhiteSpace(identifier))
                return Ok(new { allowed = false, reason = "invalid_request" });

            var scannerUserId = ResolveEffectiveScannerUserId(requestBody);
            var visitBeforeScan = _visitService.GetVisitById(identifier);
            if (visitBeforeScan != null && !CanScanWorkplaceSite(visitBeforeScan.WorkplaceSiteId, scannerUserId))
            {
                return Ok(new { allowed = false, reason = "site_access_denied", identifier });
            }
            var (allowed, reason) = _visitService.RecordVisitScan(identifier, scannerUserId);
            var visit = _visitService.GetVisitById(identifier);
            return Ok(
                new
                {
                    allowed,
                    reason,
                    identifier,
                    displayName = visit?.VisitorName ?? string.Empty,
                    visitedPersonName = visit?.SubjectDisplay ?? string.Empty,
                }
            );
        }

        private static string ResolveIdentifier(string? routeIdentifier, JsonElement requestBody)
        {
            if (!string.IsNullOrWhiteSpace(routeIdentifier))
            {
                return routeIdentifier.Trim();
            }

            return ReadString(requestBody, "identifier", "qr_data", "id", "permitNumber", "visitId")
                ?? string.Empty;
        }

        private static string NormalizePermitIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return string.Empty;
            }

            return BuildPermitCode(identifier);
        }

        private static string NormalizeIdentifier(string identifier, out string scanMode)
        {
            identifier = NormalizeDigits(identifier);
            scanMode = DetectScanMode(identifier);
            if (string.Equals(scanMode, "visit", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeVisitIdentifier(identifier);
            }

            return NormalizePermitIdentifier(identifier);
        }

        private static string DetectScanMode(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return "permit";
            }

            var normalized = NormalizeDigits(identifier).Trim().ToUpperInvariant();
            if (
                normalized.StartsWith("V", StringComparison.OrdinalIgnoreCase)
                && normalized.Length > 1
            )
            {
                return "visit";
            }

            if (normalized.Contains("VISIT", StringComparison.OrdinalIgnoreCase))
            {
                return "visit";
            }

            return "permit";
        }

        private static string NormalizeVisitIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return string.Empty;
            }

            var trimmed = NormalizeDigits(identifier).Trim();
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var visitNumber))
            {
                return $"V{visitNumber}";
            }

            return trimmed;
        }

        private static string BuildPermitCode(string identifier)
        {
            var trimmed = NormalizeDigits(identifier).Trim();
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var publicNumber))
            {
                return $"PERMIT-{publicNumber:D5}";
            }

            return string.Empty;
        }

        private bool CanScanWorkplaceSite(int? recordSiteId, string? scannerUserId)
        {
            var scanner = string.IsNullOrWhiteSpace(scannerUserId)
                ? null
                : _userAdminService.GetUserAccount(scannerUserId);
            if (
                scanner != null
                && !scanner.IsSuperAdmin
                && !string.Equals(
                    scanner.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                )
                && scanner.WorkplaceSiteId != recordSiteId
            )
            {
                return false;
            }

            var device = _displayDeviceService.GetApprovedDevice(HttpContext);
            return device == null || device.WorkplaceSiteId == recordSiteId;
        }

        private (string Identifier, string ScanMode) ResolveAutoIdentifier(string rawIdentifier)
        {
            var normalizedRaw = NormalizeDigits(rawIdentifier).Trim();
            var identifier = NormalizeIdentifier(normalizedRaw, out var scanMode);
            if (!IsDigitsOnly(normalizedRaw))
            {
                return (identifier, scanMode);
            }

            var visitIdentifier = NormalizeVisitIdentifier(normalizedRaw);
            var permitExists = !string.IsNullOrWhiteSpace(identifier)
                && _permitService.GetPermitByNumber(identifier) != null;
            var visitExists = !string.IsNullOrWhiteSpace(visitIdentifier)
                && _visitService.GetVisitById(visitIdentifier) != null;
            return visitExists && !permitExists
                ? (visitIdentifier, "visit")
                : (identifier, scanMode);
        }

        private static bool IsDigitsOnly(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.All(char.IsDigit);

        private static string NormalizeDigits(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Concat(
                value.Select(character => character switch
                {
                    >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
                    >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
                    _ => character,
                })
            );
        }

        private static string? ResolveScannerUserId(JsonElement requestBody)
        {
            return ReadString(
                requestBody,
                "scannerUserId",
                "scanner_user_id",
                "scannerUser",
                "scanner_user"
            );
        }

        private string? ResolveEffectiveScannerUserId(JsonElement requestBody)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return User.Identity.Name;
            }

            return ResolveScannerUserId(requestBody);
        }

        private PermitScanAuditContext BuildPermitAuditContext(
            JsonElement requestBody,
            string defaultGateName,
            string defaultExecutionMethod
        )
        {
            var requestedDeviceId =
                ReadString(requestBody, "deviceId", "device")
                ?? Request.Headers.UserAgent.ToString();
            var deviceId = ResolveScanSessionKey(NormalizeAuditValue(requestedDeviceId, 180));
            var displayOperator = _userAdminService.GetDisplayOperatorSession(deviceId);
            var operatorAccount =
                displayOperator?.Username
                ?? User.Identity?.Name
                ?? ResolveScannerUserId(requestBody)
                ?? string.Empty;
            var operatorName =
                displayOperator?.DisplayName
                ?? User.Claims.FirstOrDefault(claim => claim.Type == "DisplayName")?.Value
                ?? operatorAccount;

            var requestedExecutionMethod = ReadString(
                requestBody,
                "executionMethod",
                "method"
            );
            var executionMethod = requestedExecutionMethod switch
            {
                "camera" => "camera",
                "barcode_scanner" => "barcode_scanner",
                "manual" => "manual",
                "scan" => "scan",
                _ => defaultExecutionMethod,
            };

            return new PermitScanAuditContext
            {
                GateName = NormalizeAuditValue(
                    ReadString(requestBody, "gateName", "gate", "source") ?? defaultGateName,
                    128
                ),
                GateOperatorName = NormalizeAuditValue(operatorName, 128),
                GateOperatorAccount = NormalizeAuditValue(operatorAccount, 128),
                DeviceId = deviceId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                ExecutionMethod = executionMethod,
                IsAutomated = false,
            };
        }

        private static string NormalizeAuditValue(string? value, int maxLength)
        {
            var normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }

        private bool HasScanAccess()
        {
            if (
                User.Identity?.IsAuthenticated == true
                && User.HasPermission(AppPermissions.ScanOperations)
            )
            {
                return true;
            }

            return _displayDeviceService.GetApprovedDevice(HttpContext) != null;
        }

        private string ResolveScanSessionKey(string? clientDeviceId)
        {
            if (
                User.Identity?.IsAuthenticated == true
                && User.HasPermission(AppPermissions.ScanOperations)
            )
            {
                return $"user:{User.Identity.Name}:{clientDeviceId}";
            }

            var approvedDevice = _displayDeviceService.GetApprovedDevice(HttpContext);
            return approvedDevice == null ? string.Empty : $"display-device:{approvedDevice.Id}";
        }

        private string MaskIdentity(string? value)
        {
            return OnlineEditionSettings.HideSensitiveIdentityFields(_configuration)
                ? string.Empty
                : MaskSensitiveValue(value, 4);
        }

        private static string MaskSensitiveValue(string? value, int visibleSuffixLength)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var suffixLength = Math.Min(Math.Max(visibleSuffixLength, 0), normalized.Length);
            return new string('*', normalized.Length - suffixLength) + normalized[^suffixLength..];
        }

        private bool RequiresActiveDisplayOperatorForScan()
        {
            return !(
                User.Identity?.IsAuthenticated == true
                && User.HasPermission(AppPermissions.ScanOperations)
            );
        }

        private DisplayOperatorSessionInfo? ResolveActiveDisplayOperatorForScan(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return null;
            }

            return _userAdminService.GetDisplayOperatorSession(deviceId);
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

            if (!Uri.TryCreate(identifier.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            var permitIdFromQuery = uri
                .Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .FirstOrDefault(part =>
                    part.Length == 2
                    && string.Equals(part[0], "id", StringComparison.OrdinalIgnoreCase)
                )
                ?[1];

            if (!string.IsNullOrWhiteSpace(permitIdFromQuery))
            {
                permitCode = Uri.UnescapeDataString(permitIdFromQuery.Replace("+", " "));
                return !string.IsNullOrWhiteSpace(permitCode);
            }

            var pathSegments = uri
                .AbsolutePath.Trim('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            var verifyByNumberIndex = Array.FindIndex(
                pathSegments,
                segment =>
                    string.Equals(segment, "VerifyByNumber", StringComparison.OrdinalIgnoreCase)
            );

            if (verifyByNumberIndex >= 0 && verifyByNumberIndex < pathSegments.Length - 1)
            {
                permitCode = Uri.UnescapeDataString(pathSegments[verifyByNumberIndex + 1]);
                return !string.IsNullOrWhiteSpace(permitCode);
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

        private static bool ResolveBoolean(JsonElement requestBody, params string[] propertyNames)
        {
            if (requestBody.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!requestBody.TryGetProperty(propertyName, out var property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (property.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (
                    property.ValueKind == JsonValueKind.String
                    && bool.TryParse(property.GetString(), out var parsed)
                )
                {
                    return parsed;
                }
            }

            return false;
        }

        private bool ResolveOverrideEntry(
            JsonElement requestBody,
            PermitScanAuditContext auditContext,
            out string failureReason
        )
        {
            failureReason = string.Empty;
            var requested = ResolveBoolean(
                requestBody,
                "overrideEntry",
                "override",
                "forceOverride"
            );
            if (!requested)
            {
                return false;
            }

            if (
                User.Identity?.IsAuthenticated != true
                || !User.HasPermission(AppPermissions.ScanOperations)
            )
            {
                failureReason = "override_not_authorized";
                return false;
            }

            var reason = ReadString(
                requestBody,
                "overrideReason",
                "override_reason",
                "manualOverrideReason"
            );
            if (string.IsNullOrWhiteSpace(reason))
            {
                failureReason = "override_reason_required";
                return false;
            }

            auditContext.ManualOverrideReason = reason;
            auditContext.ExecutionMethod = "manual-override";
            auditContext.IsAutomated = false;
            return true;
        }

        private static string? ReadString(JsonElement requestBody, params string[] propertyNames)
        {
            if (requestBody.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!requestBody.TryGetProperty(propertyName, out var property))
                {
                    continue;
                }

                var value = property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString(),
                    JsonValueKind.Number => property.ToString(),
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }
    }
}
