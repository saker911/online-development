using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public sealed class TenantManagementService : ITenantManagementService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ITimeLimitedDataProtector _checkoutProtector;
        private readonly ISubscriptionPlanService? _subscriptionPlanService;
        private readonly ILogger<TenantManagementService>? _logger;

        public TenantManagementService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IDataProtectionProvider dataProtectionProvider,
            ISubscriptionPlanService? subscriptionPlanService = null,
            ILogger<TenantManagementService>? logger = null
        )
        {
            _dbContextFactory = dbContextFactory;
            _subscriptionPlanService = subscriptionPlanService;
            _logger = logger;
            _checkoutProtector = dataProtectionProvider
                .CreateProtector("VehiclePermitSystemWeb.Subscription.Checkout.v1")
                .ToTimeLimitedDataProtector();
        }

        public TenantManagementViewModel GetDashboard()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var tenants = db.Tenants.AsNoTracking().OrderBy(tenant => tenant.Name).ToList();
            var usersByTenant = db
                .UserAccounts.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(user => !user.IsSuperAdmin)
                .Select(user => new
                {
                    user.TenantId,
                    user.Username,
                    user.FullName,
                    user.DisplayName,
                    user.Role,
                    user.IsActive,
                    user.IsSuperAdmin,
                })
                .ToList()
                .GroupBy(user => user.TenantId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(user => user.Role == AppRoles.GeneralManager)
                        .ThenByDescending(user => user.IsActive)
                        .ThenBy(user => string.IsNullOrWhiteSpace(user.FullName) ? user.DisplayName : user.FullName)
                        .ThenBy(user => user.Username)
                        .Select(user => new TenantUserSummaryViewModel
                        {
                            Username = user.Username,
                            FullName = string.IsNullOrWhiteSpace(user.FullName)
                                ? string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName
                                : user.FullName,
                            RoleDisplayName = AppRoles.GetDisplayName(user.Role, user.IsSuperAdmin),
                            IsActive = user.IsActive,
                            IsTenantManager = user.Role == AppRoles.GeneralManager,
                        })
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase
                );
            var permitCounts = CountByTenant(db.Permits.IgnoreQueryFilters());
            var visitCounts = CountByTenant(db.Visits.IgnoreQueryFilters());
            var organizationNames = db
                .AdministrationSettings.IgnoreQueryFilters()
                .AsNoTracking()
                .GroupBy(settings => settings.TenantId)
                .Select(group => new
                {
                    TenantId = group.Key,
                    OrganizationName = group
                        .OrderByDescending(settings => settings.Id == 1)
                        .ThenBy(settings => settings.Id)
                        .Select(settings => settings.OrganizationName)
                        .FirstOrDefault(),
                })
                .ToDictionary(
                    item => item.TenantId,
                    item => item.OrganizationName ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase
                );

            return new TenantManagementViewModel
            {
                Tenants = tenants
                    .Select(tenant => new TenantSummaryViewModel
                    {
                        TenantId = tenant.TenantId,
                        Name = tenant.Name,
                        Slug = tenant.Slug,
                        OrganizationReference = tenant.OrganizationReference,
                        IsActive = tenant.IsActive,
                        CreatedAtUtc = tenant.CreatedAtUtc,
                        SubscriptionStatus = tenant.SubscriptionStatus,
                        PlanName = tenant.PlanName,
                        TrialEndsAtUtc = tenant.TrialEndsAtUtc,
                        SubscriptionEndsAtUtc = tenant.SubscriptionEndsAtUtc,
                        MaxUsers = tenant.MaxUsers,
                        MaxPermitsPerMonth = tenant.MaxPermitsPerMonth,
                        MaxVisitsPerMonth = tenant.MaxVisitsPerMonth,
                        PermitsServiceEnabled = tenant.PermitsServiceEnabled,
                        VisitsServiceEnabled = tenant.VisitsServiceEnabled,
                        SelfServiceEnabled = tenant.SelfServiceEnabled,
                        QueueServiceEnabled = tenant.QueueServiceEnabled,
                        GateServiceEnabled = tenant.GateServiceEnabled,
                        IsBlockedBySubscription = IsBlockedBySubscription(tenant),
                        Users = usersByTenant.TryGetValue(tenant.TenantId, out var tenantUsers)
                            ? tenantUsers
                            : new List<TenantUserSummaryViewModel>(),
                        UserCount = usersByTenant.TryGetValue(tenant.TenantId, out var users)
                            ? users.Count
                            : 0,
                        PermitCount = GetCount(permitCounts, tenant.TenantId),
                        VisitCount = GetCount(visitCounts, tenant.TenantId),
                        OrganizationName = organizationNames.TryGetValue(
                            tenant.TenantId,
                            out var organizationName
                        )
                            ? organizationName
                            : string.Empty,
                    })
                    .ToList(),
            };
        }

        public TenantEditorViewModel? GetEditor(string tenantId)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedTenantId = NormalizeKey(tenantId);
            var tenant = db
                .Tenants.AsNoTracking()
                .FirstOrDefault(item => item.TenantId == normalizedTenantId);
            if (tenant == null)
            {
                return null;
            }

            var settings = db
                .AdministrationSettings.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenant.TenantId)
                .OrderByDescending(item => item.Id == 1)
                .ThenBy(item => item.Id)
                .FirstOrDefault();

            return new TenantEditorViewModel
            {
                TenantId = tenant.TenantId,
                Name = tenant.Name,
                Slug = tenant.Slug,
                OrganizationReference = tenant.OrganizationReference,
                DepartmentName = settings?.DepartmentName ?? string.Empty,
                SubscriptionStatus = TenantSubscriptionStatuses.Normalize(
                    tenant.SubscriptionStatus
                ),
                PlanName = string.IsNullOrWhiteSpace(tenant.PlanName)
                    ? TenantDefaults.DefaultPlanName
                    : tenant.PlanName,
                TrialEndsAtUtc = tenant.TrialEndsAtUtc,
                SubscriptionEndsAtUtc = tenant.SubscriptionEndsAtUtc,
                MaxUsers = tenant.MaxUsers,
                MaxPermitsPerMonth = tenant.MaxPermitsPerMonth,
                MaxVisitsPerMonth = tenant.MaxVisitsPerMonth,
                PermitsServiceEnabled = tenant.PermitsServiceEnabled,
                VisitsServiceEnabled = tenant.VisitsServiceEnabled,
                SelfServiceEnabled = tenant.SelfServiceEnabled,
                QueueServiceEnabled = tenant.QueueServiceEnabled,
                GateServiceEnabled = tenant.GateServiceEnabled,
                NotificationCenterEnabled = tenant.NotificationCenterEnabled,
                PermitNotificationsEnabled = tenant.PermitNotificationsEnabled,
                VisitNotificationsEnabled = tenant.VisitNotificationsEnabled,
                SecurityAlertsEnabled = tenant.SecurityAlertsEnabled,
                FailedOperationAlertsEnabled = tenant.FailedOperationAlertsEnabled,
                UnauthorizedMovementAlertsEnabled = tenant.UnauthorizedMovementAlertsEnabled,
                NotificationRetentionDays = tenant.NotificationRetentionDays,
                EmailOutboxRetentionDays = tenant.EmailOutboxRetentionDays,
                AuditLogRetentionDays = tenant.AuditLogRetentionDays,
            };
        }

        public TenantOperationResult CreateTenant(TenantEditorViewModel model)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var tenantId = NormalizeKey(model.TenantId);
            var name = NormalizeText(model.Name);
            var slug = NormalizeKey(model.Slug);
            var organizationReference = NormalizeNumericReference(model.OrganizationReference);
            var departmentName = NormalizeText(model.DepartmentName);
            if (string.IsNullOrWhiteSpace(departmentName))
            {
                departmentName = "الإدارة العامة";
            }
            var subscriptionStatus = TenantSubscriptionStatuses.Normalize(model.SubscriptionStatus);
            var planName = NormalizeText(model.PlanName);
            NormalizeServiceDependencies(model);

            if (string.IsNullOrWhiteSpace(name))
            {
                return new TenantOperationResult(false, "اسم الجهة مطلوب.");
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                tenantId = GenerateTenantKey(db, "org");
            }

            if (string.IsNullOrWhiteSpace(slug))
            {
                slug = GenerateTenantKey(db, "workspace");
            }

            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(slug))
            {
                return new TenantOperationResult(
                    false,
                    "تعذر توليد معرف الجهة. أدخل معرفًا مختصرًا مثل company-1."
                );
            }

            if (db.Tenants.Any(tenant => tenant.TenantId == tenantId))
            {
                return new TenantOperationResult(false, "معرف الجهة مستخدم مسبقًا.");
            }

            if (db.Tenants.Any(tenant => tenant.Slug == slug))
            {
                return new TenantOperationResult(false, "الرابط المختصر مستخدم مسبقًا.");
            }

            db.Tenants.Add(
                new Tenant
                {
                    TenantId = tenantId,
                    Name = name,
                    Slug = slug,
                    OrganizationReference = organizationReference,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    SubscriptionStatus = subscriptionStatus,
                    PlanName = string.IsNullOrWhiteSpace(planName)
                        ? TenantDefaults.DefaultPlanName
                        : planName,
                    TrialEndsAtUtc = NormalizeDate(model.TrialEndsAtUtc),
                    SubscriptionEndsAtUtc = NormalizeDate(model.SubscriptionEndsAtUtc),
                    MaxUsers = NormalizeLimit(model.MaxUsers),
                    MaxPermitsPerMonth = NormalizeLimit(model.MaxPermitsPerMonth),
                    MaxVisitsPerMonth = NormalizeLimit(model.MaxVisitsPerMonth),
                    PermitsServiceEnabled = model.PermitsServiceEnabled,
                    VisitsServiceEnabled = model.VisitsServiceEnabled,
                    SelfServiceEnabled = model.SelfServiceEnabled,
                    QueueServiceEnabled = model.QueueServiceEnabled,
                    GateServiceEnabled = model.GateServiceEnabled,
                    NotificationCenterEnabled = model.NotificationCenterEnabled,
                    PermitNotificationsEnabled = model.PermitNotificationsEnabled,
                    VisitNotificationsEnabled = model.VisitNotificationsEnabled,
                    SecurityAlertsEnabled = model.SecurityAlertsEnabled,
                    FailedOperationAlertsEnabled = model.FailedOperationAlertsEnabled,
                    UnauthorizedMovementAlertsEnabled = model.UnauthorizedMovementAlertsEnabled,
                    NotificationRetentionDays = Math.Clamp(model.NotificationRetentionDays, 7, 730),
                    EmailOutboxRetentionDays = Math.Clamp(model.EmailOutboxRetentionDays, 7, 365),
                    AuditLogRetentionDays = Math.Clamp(model.AuditLogRetentionDays, 90, 2555),
                }
            );

            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = GetNextAdministrationSettingsId(db),
                    TenantId = tenantId,
                    OrganizationName = name,
                    DepartmentName = departmentName,
                    DisplayAccessKey = DisplayAccessKeyHasher.Hash(
                        DisplayAccessDefaults.CreateAccessKey()
                    ),
                    OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
                }
            );
            db.Departments.Add(
                new Department
                {
                    TenantId = tenantId,
                    Name = departmentName,
                    IsActive = true,
                }
            );

            var ownerUsername = NormalizeText(model.OwnerUsername);
            var ownerFullName = NormalizeText(model.OwnerFullName);
            var ownerEmail = NormalizeText(model.OwnerEmail).ToLowerInvariant();
            var ownerPhone = NormalizeText(model.OwnerPhoneNumber);
            var ownerRequested = !string.IsNullOrWhiteSpace(ownerUsername)
                || !string.IsNullOrWhiteSpace(ownerFullName)
                || !string.IsNullOrWhiteSpace(ownerEmail)
                || !string.IsNullOrWhiteSpace(ownerPhone);
            if (!ownerRequested)
            {
                return new TenantOperationResult(false, "بيانات مسؤول الجهة مطلوبة عند إنشاء جهة جديدة.");
            }
            if (ownerRequested)
            {
                if (!NewAccountUsernameValidator.IsValid(ownerUsername))
                {
                    return new TenantOperationResult(false, NewAccountUsernameValidator.ErrorMessage);
                }
                if (string.IsNullOrWhiteSpace(ownerFullName))
                {
                    return new TenantOperationResult(false, "اسم مسؤول الجهة مطلوب.");
                }
                if (!new EmailAddressAttribute().IsValid(ownerEmail))
                {
                    return new TenantOperationResult(false, "البريد الإلكتروني لمسؤول الجهة غير صحيح.");
                }
                if (!SaudiMobileNumberValidator.IsValidRequired(ownerPhone))
                {
                    return new TenantOperationResult(false, SaudiMobileNumberValidator.ErrorMessage);
                }
                if (db.UserAccounts.IgnoreQueryFilters().Any(user => user.Username == ownerUsername))
                {
                    return new TenantOperationResult(false, "اسم مستخدم مسؤول الجهة مستخدم في حساب آخر.");
                }

                var owner = new UserAccount
                {
                    TenantId = tenantId,
                    Username = ownerUsername,
                    DisplayName = ownerFullName,
                    FullName = ownerFullName,
                    Department = departmentName,
                    JobTitle = "مسؤول الجهة",
                    PhoneNumber = ownerPhone,
                    Email = ownerEmail,
                    IsEmailConfirmed = true,
                    IsActive = true,
                    Role = AppRoles.GeneralManager,
                };
                AppPermissions.ApplyRoleDefaults(owner);
                UserAccountService.SetPassword(
                    owner,
                    $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}"
                );
                db.UserAccounts.Add(owner);

                var settings = db.ChangeTracker.Entries<AdministrationSettings>()
                    .Select(entry => entry.Entity)
                    .First(item => item.TenantId == tenantId);
                settings.GeneralManagerUsername = ownerUsername;
                settings.ManagerName = ownerFullName;
                settings.ManagerTitle = "مسؤول الجهة";
                settings.ManagerPhoneNumber = ownerPhone;
                settings.Email = ownerEmail;
                settings.IsInitialSetupCompleted = true;
            }

            db.SaveChanges();
            return new TenantOperationResult(
                true,
                "تمت إضافة الجهة وربط حساب المسؤول بها.",
                tenantId,
                ownerUsername
            );
        }

        public TenantSignupResult CreateSignup(
            TenantSignupViewModel model,
            bool emailConfirmed = false
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var plan = FindPlan(model.PlanCode);
            if (plan == null)
            {
                return new TenantSignupResult(false, "اختر باقة صحيحة لإكمال التسجيل.");
            }

            var companyName = NormalizeText(model.CompanyName);
            var tenantId = GenerateTenantKey(db, "org");
            var slug = GenerateTenantKey(db, "workspace");
            var organizationReference = NormalizeNumericReference(model.OrganizationReference);
            var ownerUsername = NormalizeText(model.OwnerUsername);
            var ownerFullName = NormalizeText(model.OwnerFullName);
            var ownerPhone = NormalizeText(model.OwnerPhoneNumber);
            var ownerEmail = NormalizeText(model.OwnerEmail);
            var password = model.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(companyName))
            {
                return new TenantSignupResult(false, "اسم الجهة أو الموقع مطلوب.");
            }

            if (!NewAccountUsernameValidator.IsValid(ownerUsername))
            {
                return new TenantSignupResult(false, NewAccountUsernameValidator.ErrorMessage);
            }

            if (!SaudiMobileNumberValidator.IsValidRequired(ownerPhone))
            {
                return new TenantSignupResult(false, SaudiMobileNumberValidator.ErrorMessage);
            }

            if (!PasswordValidationRules.IsStrongPassword(password))
            {
                return new TenantSignupResult(
                    false,
                    "كلمة المرور يجب أن تكون 8 أحرف على الأقل وتحتوي على حرف كبير وحرف صغير ورقم ورمز خاص."
                );
            }

            if (!model.AcceptPolicy)
            {
                return new TenantSignupResult(false, "يجب الموافقة على سياسة الاشتراك والدفع.");
            }

            var now = DateTime.UtcNow;
            PurgeExpiredPendingSignups(db, now);

            if (db.UserAccounts.IgnoreQueryFilters().Any(user => user.Username == ownerUsername))
            {
                return new TenantSignupResult(false, "اسم المستخدم مستخدم مسبقًا.");
            }

            db.Tenants.Add(
                new Tenant
                {
                    TenantId = tenantId,
                    Name = companyName,
                    Slug = slug,
                    OrganizationReference = organizationReference,
                    IsActive = true,
                    CreatedAtUtc = now,
                    SubscriptionStatus = TenantSubscriptionStatuses.PendingPayment,
                    SignupExpiresAtUtc = now.AddHours(24),
                    PlanName = plan.Name,
                    SignupPlanCode = plan.Code,
                    SignupPlanPrice = plan.TotalPrice,
                    SignupPlanDurationMonths = plan.DurationMonths,
                    MaxUsers = null,
                    MaxPermitsPerMonth = null,
                    MaxVisitsPerMonth = null,
                }
            );

            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = GetNextAdministrationSettingsId(db),
                    TenantId = tenantId,
                    OrganizationName = companyName,
                    DepartmentName = "الإدارة العامة",
                    GeneralManagerUsername = ownerUsername,
                    ManagerName = ownerFullName,
                    ManagerTitle = "مالك الحساب",
                    ManagerPhoneNumber = ownerPhone,
                    DisplayAccessKey = DisplayAccessKeyHasher.Hash(
                        DisplayAccessDefaults.CreateAccessKey()
                    ),
                    OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
                    IsInitialSetupCompleted = true,
                }
            );

            db.Departments.Add(
                new Department
                {
                    TenantId = tenantId,
                    Name = "الإدارة العامة",
                    IsActive = true,
                }
            );

            var owner = new UserAccount
            {
                TenantId = tenantId,
                Username = ownerUsername,
                DisplayName = ownerFullName,
                FullName = ownerFullName,
                Department = "الإدارة العامة",
                JobTitle = "مالك الحساب",
                PhoneNumber = ownerPhone,
                Email = ownerEmail,
                IsEmailConfirmed = emailConfirmed,
                IsActive = true,
                Role = AppRoles.GeneralManager,
                ManagerUsername = string.Empty,
            };
            AppPermissions.ClearAll(owner);
            UserAccountService.SetPassword(owner, password);
            db.UserAccounts.Add(owner);

            db.SaveChanges();
            return new TenantSignupResult(
                true,
                "تم إنشاء الحساب وبانتظار إتمام الدفع.",
                tenantId,
                BuildPaymentReference(tenantId, now),
                _checkoutProtector.Protect(tenantId, TimeSpan.FromMinutes(30)),
                ownerUsername
            );
        }

        public TenantSignupResult CreateGoogleTrial(string fullName, string email)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedEmail = NormalizeText(email).ToLowerInvariant();
            var normalizedName = NormalizeText(fullName);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return new TenantSignupResult(
                    false,
                    "لم يرسل Google بريدًا إلكترونيًا صالحًا للحساب."
                );
            }

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                normalizedName = normalizedEmail.Split('@', 2)[0];
            }

            var now = DateTime.UtcNow;
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            var tenantId = $"trial-{suffix}";
            while (db.Tenants.Any(tenant => tenant.TenantId == tenantId || tenant.Slug == tenantId))
            {
                suffix = Convert
                    .ToHexString(RandomNumberGenerator.GetBytes(6))
                    .ToLowerInvariant();
                tenantId = $"trial-{suffix}";
            }

            var usernameHash = SHA256.HashData(
                Encoding.UTF8.GetBytes($"{normalizedEmail}:{suffix}")
            );
            var ownerUsername = $"g-{Convert.ToHexString(usernameHash)[..16].ToLowerInvariant()}";
            var organizationName = $"{normalizedName} - تجربة";

            db.Tenants.Add(
                new Tenant
                {
                    TenantId = tenantId,
                    Name = organizationName,
                    Slug = tenantId,
                    IsActive = true,
                    CreatedAtUtc = now,
                    SubscriptionStatus = TenantSubscriptionStatuses.Trial,
                    TrialEndsAtUtc = now.AddDays(2),
                    PlanName = "تجربة يومين",
                    MaxUsers = null,
                    MaxPermitsPerMonth = null,
                    MaxVisitsPerMonth = null,
                }
            );

            db.AdministrationSettings.Add(
                new AdministrationSettings
                {
                    Id = GetNextAdministrationSettingsId(db),
                    TenantId = tenantId,
                    OrganizationName = organizationName,
                    DepartmentName = "الإدارة العامة",
                    GeneralManagerUsername = ownerUsername,
                    ManagerName = normalizedName,
                    ManagerTitle = "مدير الجهة",
                    ManagerPhoneNumber = string.Empty,
                    Email = normalizedEmail,
                    DisplayAccessKey = DisplayAccessKeyHasher.Hash(
                        DisplayAccessDefaults.CreateAccessKey()
                    ),
                    OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
                    IsInitialSetupCompleted = true,
                }
            );
            db.Departments.Add(
                new Department
                {
                    TenantId = tenantId,
                    Name = "الإدارة العامة",
                    IsActive = true,
                }
            );

            var owner = new UserAccount
            {
                TenantId = tenantId,
                Username = ownerUsername,
                DisplayName = normalizedName,
                FullName = normalizedName,
                Department = "الإدارة العامة",
                JobTitle = "مدير الجهة",
                PhoneNumber = string.Empty,
                Email = normalizedEmail,
                IsEmailConfirmed = true,
                IsActive = true,
                Role = AppRoles.GeneralManager,
                ManagerUsername = string.Empty,
            };
            AppPermissions.ApplyRoleDefaults(owner);
            db.UserAccounts.Add(owner);
            db.SaveChanges();

            return new TenantSignupResult(
                true,
                "تم إنشاء مساحة التجربة لمدة يومين.",
                tenantId,
                OwnerUsername: ownerUsername
            );
        }

        public TenantCheckoutViewModel? GetCheckout(string tenantId, string checkoutToken)
        {
            var normalizedTenantId = NormalizeKey(tenantId);
            if (
                string.IsNullOrWhiteSpace(normalizedTenantId)
                || string.IsNullOrWhiteSpace(checkoutToken)
                || !IsValidCheckoutToken(normalizedTenantId, checkoutToken)
            )
            {
                return null;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var tenant = db
                .Tenants.AsNoTracking()
                .FirstOrDefault(item => item.TenantId == normalizedTenantId);
            if (tenant == null)
            {
                return null;
            }

            var plan = FindPlan(tenant.SignupPlanCode)
                ?? GetPlans().FirstOrDefault(item =>
                    string.Equals(item.Name, tenant.PlanName, StringComparison.Ordinal)
                );
            var hasSignupSnapshot = !string.IsNullOrWhiteSpace(tenant.SignupPlanCode);
            var owner = db
                .UserAccounts.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenant.TenantId)
                .OrderByDescending(item => item.Role == AppRoles.GeneralManager)
                .ThenByDescending(item => item.IsSuperAdmin)
                .FirstOrDefault();

            return new TenantCheckoutViewModel
            {
                TenantId = tenant.TenantId,
                CompanyName = tenant.Name,
                PlanName = tenant.PlanName,
                SubscriptionStatus = tenant.SubscriptionStatus,
                DurationMonths = hasSignupSnapshot
                    ? tenant.SignupPlanDurationMonths ?? plan?.DurationMonths ?? 0
                    : plan?.DurationMonths ?? 0,
                TotalPrice = hasSignupSnapshot
                    ? tenant.SignupPlanPrice ?? plan?.TotalPrice ?? 0
                    : plan?.TotalPrice ?? 0,
                PaymentReference = BuildPaymentReference(tenant.TenantId, tenant.CreatedAtUtc),
                LoginUrl = $"/o/{Uri.EscapeDataString(tenant.Slug)}",
                CheckoutToken = checkoutToken,
                OwnerUsername = owner?.Username ?? string.Empty,
                OwnerEmail = owner?.Email ?? string.Empty,
                IsEmailConfirmed = owner?.IsEmailConfirmed ?? false,
            };
        }

        private bool IsValidCheckoutToken(string tenantId, string checkoutToken)
        {
            try
            {
                var protectedTenantId = _checkoutProtector.Unprotect(
                    checkoutToken,
                    out var expiresAt
                );
                return expiresAt > DateTimeOffset.UtcNow
                    && string.Equals(
                        NormalizeKey(protectedTenantId),
                        tenantId,
                        StringComparison.Ordinal
                    );
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        public TenantOperationResult UpdateTenant(string tenantId, TenantEditorViewModel model)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedTenantId = NormalizeKey(tenantId);
            var tenant = db.Tenants.FirstOrDefault(item => item.TenantId == normalizedTenantId);
            if (tenant == null)
            {
                return new TenantOperationResult(false, "تعذر العثور على الجهة.");
            }

            var name = NormalizeText(model.Name);
            var slug = NormalizeKey(model.Slug);
            var organizationReference = NormalizeNumericReference(model.OrganizationReference);
            var departmentName = NormalizeText(model.DepartmentName);
            var subscriptionStatus = TenantSubscriptionStatuses.Normalize(model.SubscriptionStatus);
            var planName = NormalizeText(model.PlanName);
            NormalizeServiceDependencies(model);

            if (string.IsNullOrWhiteSpace(name))
            {
                return new TenantOperationResult(false, "اسم الجهة مطلوب.");
            }

            if (string.IsNullOrWhiteSpace(slug))
            {
                slug = tenant.TenantId;
            }

            if (
                db.Tenants.Any(item =>
                    item.TenantId != tenant.TenantId && item.Slug == slug
                )
            )
            {
                return new TenantOperationResult(false, "الرابط المختصر مستخدم مسبقًا.");
            }

            tenant.Name = name;
            tenant.Slug = slug;
            tenant.OrganizationReference = organizationReference;
            tenant.SubscriptionStatus = subscriptionStatus;
            tenant.PlanName = string.IsNullOrWhiteSpace(planName)
                ? TenantDefaults.DefaultPlanName
                : planName;
            tenant.TrialEndsAtUtc = NormalizeDate(model.TrialEndsAtUtc);
            tenant.SubscriptionEndsAtUtc = NormalizeDate(model.SubscriptionEndsAtUtc);
            tenant.MaxUsers = NormalizeLimit(model.MaxUsers);
            tenant.MaxPermitsPerMonth = NormalizeLimit(model.MaxPermitsPerMonth);
            tenant.MaxVisitsPerMonth = NormalizeLimit(model.MaxVisitsPerMonth);
            tenant.PermitsServiceEnabled = model.PermitsServiceEnabled;
            tenant.VisitsServiceEnabled = model.VisitsServiceEnabled;
            tenant.SelfServiceEnabled = model.SelfServiceEnabled;
            tenant.QueueServiceEnabled = model.QueueServiceEnabled;
            tenant.GateServiceEnabled = model.GateServiceEnabled;
            tenant.NotificationCenterEnabled = model.NotificationCenterEnabled;
            tenant.PermitNotificationsEnabled = model.PermitNotificationsEnabled;
            tenant.VisitNotificationsEnabled = model.VisitNotificationsEnabled;
            tenant.SecurityAlertsEnabled = model.SecurityAlertsEnabled;
            tenant.FailedOperationAlertsEnabled = model.FailedOperationAlertsEnabled;
            tenant.UnauthorizedMovementAlertsEnabled = model.UnauthorizedMovementAlertsEnabled;
            tenant.NotificationRetentionDays = Math.Clamp(model.NotificationRetentionDays, 7, 730);
            tenant.EmailOutboxRetentionDays = Math.Clamp(model.EmailOutboxRetentionDays, 7, 365);
            tenant.AuditLogRetentionDays = Math.Clamp(model.AuditLogRetentionDays, 90, 2555);

            var settings = db
                .AdministrationSettings.IgnoreQueryFilters()
                .Where(item => item.TenantId == tenant.TenantId)
                .OrderByDescending(item => item.Id == 1)
                .ThenBy(item => item.Id)
                .FirstOrDefault();
            if (settings == null)
            {
                db.AdministrationSettings.Add(
                    new AdministrationSettings
                    {
                        Id = GetNextAdministrationSettingsId(db),
                        TenantId = tenant.TenantId,
                        OrganizationName = name,
                        DepartmentName = departmentName,
                        DisplayAccessKey = DisplayAccessKeyHasher.Hash(
                            DisplayAccessDefaults.CreateAccessKey()
                        ),
                        OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
                    }
                );
            }
            else
            {
                settings.OrganizationName = name;
                settings.DepartmentName = departmentName;
                AdministrationSettingsService.NormalizeAdministrationSettings(settings);
            }

            db.SaveChanges();
            return new TenantOperationResult(true, "تم حفظ بيانات الجهة.");
        }

        public TenantOperationResult SetTenantActive(string tenantId, bool isActive)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedTenantId = NormalizeKey(tenantId);
            var tenant = db.Tenants.FirstOrDefault(item => item.TenantId == normalizedTenantId);
            if (tenant == null)
            {
                return new TenantOperationResult(false, "تعذر العثور على الجهة.");
            }

            if (
                !isActive
                && string.Equals(
                    tenant.TenantId,
                    TenantDefaults.DefaultTenantId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return new TenantOperationResult(false, "لا يمكن إيقاف الجهة الافتراضية.");
            }

            tenant.IsActive = isActive;
            if (!isActive)
            {
                db.SessionRecords.RemoveRange(
                    db.SessionRecords
                        .IgnoreQueryFilters()
                        .Where(session => session.TenantId == tenant.TenantId)
                );
            }
            db.SaveChanges();
            return new TenantOperationResult(
                true,
                isActive
                    ? "تم تفعيل الجهة."
                    : "تم إيقاف الجهة وإغلاق جلسات حساباتها."
            );
        }

        public TenantOperationResult DeleteTenant(
            string tenantId,
            string deletionReason,
            bool permanentDeletionConfirmed
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedTenantId = NormalizeKey(tenantId);
            var tenant = db.Tenants.FirstOrDefault(item => item.TenantId == normalizedTenantId);
            if (tenant == null)
            {
                return new TenantOperationResult(false, "تعذر العثور على الجهة.");
            }

            if (
                string.Equals(
                    tenant.TenantId,
                    TenantDefaults.DefaultTenantId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return new TenantOperationResult(false, "لا يمكن حذف الجهة الافتراضية.");
            }

            if (tenant.IsActive)
            {
                return new TenantOperationResult(false, "يجب إيقاف الجهة قبل حذفها.");
            }

            var normalizedReason = (deletionReason ?? string.Empty).Trim();
            if (normalizedReason.Length < 5 || normalizedReason.Length > 300)
            {
                return new TenantOperationResult(false, "اكتب سبب الحذف بوضوح من 5 إلى 300 حرف.");
            }

            if (!permanentDeletionConfirmed)
            {
                return new TenantOperationResult(false, "يجب تأكيد فهمك أن الحذف نهائي.");
            }

            using var transaction = db.Database.IsRelational()
                ? db.Database.BeginTransaction()
                : null;

            db.PermitActivities.RemoveRange(
                db.PermitActivities.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.PersonPhotos.RemoveRange(
                db.PersonPhotos.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.PersonProfiles.RemoveRange(
                db.PersonProfiles.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.VisitCompanions.RemoveRange(
                db.VisitCompanions.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.DelegationPermissions.RemoveRange(
                db.DelegationPermissions.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.Permits.RemoveRange(
                db.Permits.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.Visits.RemoveRange(
                db.Visits.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.AuditLogs.RemoveRange(
                db.AuditLogs.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.UserActivities.RemoveRange(
                db.UserActivities.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.SessionRecords.RemoveRange(
                db.SessionRecords.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.Delegations.RemoveRange(
                db.Delegations.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.DisplayDevices.RemoveRange(
                db.DisplayDevices.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.EmergencySessionMembers.RemoveRange(
                db.EmergencySessionMembers.IgnoreQueryFilters()
                    .Where(item => item.TenantId == tenant.TenantId)
            );
            db.EmergencySessions.RemoveRange(
                db.EmergencySessions.IgnoreQueryFilters()
                    .Where(item => item.TenantId == tenant.TenantId)
            );
            db.WorkplaceSiteEntrances.RemoveRange(
                db.WorkplaceSiteEntrances.IgnoreQueryFilters()
                    .Where(item => item.TenantId == tenant.TenantId)
            );
            db.WorkplaceSites.RemoveRange(
                db.WorkplaceSites.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.DisplaySecuritySettings.RemoveRange(
                db.DisplaySecuritySettings.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.ExternalUserLogins.RemoveRange(
                db.ExternalUserLogins.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.InAppNotifications.RemoveRange(
                db.InAppNotifications.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.EmailNotificationOutbox.RemoveRange(
                db.EmailNotificationOutbox.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.UserAccounts.RemoveRange(
                db.UserAccounts.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.Departments.RemoveRange(
                db.Departments.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.AdministrationSettings.RemoveRange(
                db.AdministrationSettings.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.Tenants.Remove(tenant);
            db.SaveChanges();
            transaction?.Commit();

            _logger?.LogWarning(
                "Platform owner permanently deleted tenant {TenantId} ({TenantName}). Reason: {DeletionReason}",
                tenant.TenantId,
                tenant.Name,
                normalizedReason
            );

            return new TenantOperationResult(true, "تم حذف الجهة وجميع بياناتها نهائيًا.");
        }

        public TenantOperationResult ActivatePaidSubscription(string tenantId)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedTenantId = NormalizeKey(tenantId);
            var tenant = db.Tenants.FirstOrDefault(item => item.TenantId == normalizedTenantId);
            if (tenant == null)
            {
                return new TenantOperationResult(false, "تعذر العثور على الجهة.");
            }

            var activatePendingSignupOwner =
                tenant.SignupExpiresAtUtc.HasValue
                && string.Equals(
                    tenant.SubscriptionStatus,
                    TenantSubscriptionStatuses.PendingPayment,
                    StringComparison.Ordinal
                );
            tenant.IsActive = true;
            tenant.SubscriptionStatus = TenantSubscriptionStatuses.Active;
            tenant.TrialEndsAtUtc = null;
            var durationMonths = tenant.SignupPlanDurationMonths
                ?? FindPlan(tenant.SignupPlanCode)?.DurationMonths
                ?? 1;
            tenant.SubscriptionEndsAtUtc = DateTime.UtcNow.AddMonths(
                Math.Clamp(durationMonths, 1, 120)
            );
            tenant.SignupExpiresAtUtc = null;
            if (activatePendingSignupOwner)
            {
                var pendingOwner = db
                    .UserAccounts.IgnoreQueryFilters()
                    .FirstOrDefault(user => user.TenantId == tenant.TenantId);
                if (pendingOwner != null)
                {
                    pendingOwner.IsActive = true;
                    AppPermissions.ApplyRoleDefaults(pendingOwner);
                }
            }
            db.SaveChanges();
            return new TenantOperationResult(true, "تم تفعيل الاشتراك بعد تأكيد الدفع.");
        }

        private static Dictionary<string, int> CountByTenant<TEntity>(IQueryable<TEntity> query)
            where TEntity : class, ITenantScopedEntity
        {
            return query
                .AsNoTracking()
                .GroupBy(item => item.TenantId)
                .Select(group => new { TenantId = group.Key, Count = group.Count() })
                .ToDictionary(
                    item => item.TenantId,
                    item => item.Count,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private IReadOnlyList<TenantPlanViewModel> GetPlans() =>
            _subscriptionPlanService?.GetPublicPlans() ?? TenantPlanCatalog.GetPlans();

        private TenantPlanViewModel? FindPlan(string? code) =>
            _subscriptionPlanService?.Find(code) ?? TenantPlanCatalog.Find(code);

        private static int GetCount(IReadOnlyDictionary<string, int> counts, string tenantId)
        {
            return counts.TryGetValue(tenantId, out var count) ? count : 0;
        }

        private static void PurgeExpiredPendingSignups(ApplicationDbContext db, DateTime now)
        {
            var expiredTenantIds = db
                .Tenants.IgnoreQueryFilters()
                .Where(tenant =>
                    tenant.SubscriptionStatus == TenantSubscriptionStatuses.PendingPayment
                    && tenant.SignupExpiresAtUtc.HasValue
                    && tenant.SignupExpiresAtUtc.Value <= now
                )
                .Select(tenant => tenant.TenantId)
                .ToList();
            if (expiredTenantIds.Count == 0)
            {
                return;
            }

            db.ExternalUserLogins.RemoveRange(
                db.ExternalUserLogins.IgnoreQueryFilters().Where(item => expiredTenantIds.Contains(item.TenantId))
            );
            db.UserAccounts.RemoveRange(
                db.UserAccounts.IgnoreQueryFilters().Where(item => expiredTenantIds.Contains(item.TenantId))
            );
            db.AdministrationSettings.RemoveRange(
                db.AdministrationSettings.IgnoreQueryFilters().Where(item => expiredTenantIds.Contains(item.TenantId))
            );
            db.Tenants.RemoveRange(
                db.Tenants.IgnoreQueryFilters().Where(item => expiredTenantIds.Contains(item.TenantId))
            );
            db.SaveChanges();
        }

        private static bool IsBlockedBySubscription(Tenant tenant)
        {
            if (!tenant.IsActive)
            {
                return true;
            }

            var status = TenantSubscriptionStatuses.Normalize(tenant.SubscriptionStatus);
            if (
                string.Equals(status, TenantSubscriptionStatuses.Suspended, StringComparison.Ordinal)
                || string.Equals(status, TenantSubscriptionStatuses.Expired, StringComparison.Ordinal)
            )
            {
                return true;
            }

            var now = DateTime.UtcNow;
            return string.Equals(status, TenantSubscriptionStatuses.Trial, StringComparison.Ordinal)
                ? tenant.TrialEndsAtUtc.HasValue && tenant.TrialEndsAtUtc.Value < now
                : tenant.SubscriptionEndsAtUtc.HasValue && tenant.SubscriptionEndsAtUtc.Value < now;
        }

        private static int GetNextAdministrationSettingsId(ApplicationDbContext db)
        {
            var currentMaxId = db
                .AdministrationSettings.IgnoreQueryFilters()
                .Select(settings => (int?)settings.Id)
                .Max();
            return (currentMaxId ?? 0) + 1;
        }

        private static string NormalizeText(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static DateTime? NormalizeDate(DateTime? value)
        {
            return value?.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value;
        }

        private static int? NormalizeLimit(int? value)
        {
            return value is > 0 ? value : null;
        }

        private static void NormalizeServiceDependencies(TenantEditorViewModel model)
        {
            if (!model.VisitsServiceEnabled)
            {
                model.SelfServiceEnabled = false;
                model.QueueServiceEnabled = false;
                return;
            }

            if (!model.SelfServiceEnabled)
            {
                model.QueueServiceEnabled = false;
            }
        }

        private static string BuildPaymentReference(string tenantId, DateTime createdAtUtc)
        {
            return $"PAY-{tenantId.ToUpperInvariant()}-{createdAtUtc:yyyyMMddHHmm}";
        }

        private static string NormalizeKey(string? value)
        {
            var normalized = new string(
                    (value ?? string.Empty)
                        .Trim()
                        .ToLowerInvariant()
                        .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
                        .ToArray()
                )
                .Trim('-', '_');

            while (normalized.Contains("--", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
            }

            return normalized.Length <= 64 ? normalized : normalized[..64].Trim('-', '_');
        }

        private static string NormalizeNumericReference(string? value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).Take(32).ToArray());
        }

        private static string GenerateTenantKey(ApplicationDbContext db, string prefix)
        {
            string key;
            do
            {
                key = $"{prefix}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}";
            } while (db.Tenants.Any(tenant => tenant.TenantId == key || tenant.Slug == key));

            return key;
        }
    }
}
