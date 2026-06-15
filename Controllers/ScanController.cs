using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
using VehiclePermitSystemWeb.Services.Display;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

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

        public ScanController(
            IPermitService permitService,
            IVisitService visitService,
            IUserAdminService userAdminService,
            IDisplayDeviceService displayDeviceService
        )
        {
            _permitService = permitService;
            _visitService = visitService;
            _userAdminService = userAdminService;
            _displayDeviceService = displayDeviceService;
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

            var scannerUserId = ResolveScannerUserId(requestBody);
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
                            nationalId = permit.NationalId,
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

            var identifier = NormalizeIdentifier(rawIdentifier, out var scanMode);
            if (string.IsNullOrWhiteSpace(identifier))
                return Ok(new { allowed = false, reason = "invalid_request" });

            var requestedScannerUserId = ResolveScannerUserId(requestBody);
            var auditContext = BuildPermitAuditContext(requestBody, "البوابة", "scan");
            var displayOperator = ResolveActiveDisplayOperatorForScan(auditContext.DeviceId);
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
                                nationalId = visit.NationalId,
                                phoneNumber = visit.PhoneNumber,
                            },
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
                            nationalId = permit.NationalId,
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

            var scannerUserId = ResolveScannerUserId(requestBody);
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

            var normalized = identifier.Trim().ToUpperInvariant();
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

            var trimmed = identifier.Trim();
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var visitNumber))
            {
                return $"V{visitNumber}";
            }

            return trimmed;
        }

        private static string BuildPermitCode(string identifier)
        {
            var trimmed = identifier.Trim();
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var publicNumber))
            {
                return $"PERMIT-{publicNumber:D5}";
            }

            return string.Empty;
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

        private PermitScanAuditContext BuildPermitAuditContext(
            JsonElement requestBody,
            string defaultGateName,
            string defaultExecutionMethod
        )
        {
            var deviceId =
                ReadString(requestBody, "deviceId", "device")
                ?? Request.Headers.UserAgent.ToString();
            var displayOperator = _userAdminService.GetDisplayOperatorSession(deviceId);
            var operatorAccount =
                displayOperator?.Username
                ?? ResolveScannerUserId(requestBody)
                ?? User.Identity?.Name
                ?? string.Empty;
            var operatorName =
                displayOperator?.DisplayName
                ?? User.Claims.FirstOrDefault(claim => claim.Type == "DisplayName")?.Value
                ?? operatorAccount;

            return new PermitScanAuditContext
            {
                GateName = ReadString(requestBody, "gateName", "gate", "source") ?? defaultGateName,
                GateOperatorName = operatorName,
                GateOperatorAccount = operatorAccount,
                DeviceId = deviceId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                ExecutionMethod =
                    ReadString(requestBody, "executionMethod", "method") ?? defaultExecutionMethod,
                IsAutomated = false,
            };
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

            var token = uri
                .Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .FirstOrDefault(part =>
                    part.Length == 2
                    && string.Equals(part[0], "token", StringComparison.OrdinalIgnoreCase)
                )
                ?[1];

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            token = Uri.UnescapeDataString(token.Replace("+", " "));
            if (
                !_permitService.TryValidatePermitQrToken(
                    token,
                    out var permit,
                    out var status,
                    out _
                )
                || permit == null
            )
            {
                return false;
            }

            if (
                !string.Equals(status, "authorized", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase)
            )
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
