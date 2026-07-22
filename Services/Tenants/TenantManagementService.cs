using System.Security.Cryptography;
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

        public TenantManagementService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IDataProtectionProvider dataProtectionProvider
        )
        {
            _dbContextFactory = dbContextFactory;
            _checkoutProtector = dataProtectionProvider
                .CreateProtector("VehiclePermitSystemWeb.Subscription.Checkout.v1")
                .ToTimeLimitedDataProtector();
        }

        public TenantManagementViewModel GetDashboard()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var tenants = db.Tenants.AsNoTracking().OrderBy(tenant => tenant.Name).ToList();
            var userCounts = CountByTenant(db.UserAccounts.IgnoreQueryFilters());
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
                        IsActive = tenant.IsActive,
                        CreatedAtUtc = tenant.CreatedAtUtc,
                        SubscriptionStatus = tenant.SubscriptionStatus,
                        PlanName = tenant.PlanName,
                        TrialEndsAtUtc = tenant.TrialEndsAtUtc,
                        SubscriptionEndsAtUtc = tenant.SubscriptionEndsAtUtc,
                        MaxUsers = tenant.MaxUsers,
                        MaxPermitsPerMonth = tenant.MaxPermitsPerMonth,
                        MaxVisitsPerMonth = tenant.MaxVisitsPerMonth,
                        IsBlockedBySubscription = IsBlockedBySubscription(tenant),
                        UserCount = GetCount(userCounts, tenant.TenantId),
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
            };
        }

        public TenantOperationResult CreateTenant(TenantEditorViewModel model)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var tenantId = NormalizeKey(model.TenantId);
            var name = NormalizeText(model.Name);
            var slug = NormalizeKey(model.Slug);
            var departmentName = NormalizeText(model.DepartmentName);
            if (string.IsNullOrWhiteSpace(departmentName))
            {
                departmentName = "الإدارة العامة";
            }
            var subscriptionStatus = TenantSubscriptionStatuses.Normalize(model.SubscriptionStatus);
            var planName = NormalizeText(model.PlanName);

            if (string.IsNullOrWhiteSpace(name))
            {
                return new TenantOperationResult(false, "اسم الجهة مطلوب.");
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                tenantId = NormalizeKey(slug);
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                tenantId = NormalizeKey(name);
            }

            if (string.IsNullOrWhiteSpace(slug))
            {
                slug = tenantId;
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

            db.SaveChanges();
            return new TenantOperationResult(true, "تمت إضافة الجهة بنجاح.");
        }

        public TenantSignupResult CreateSignup(TenantSignupViewModel model)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var plan = TenantPlanCatalog.Find(model.PlanCode);
            if (plan == null)
            {
                return new TenantSignupResult(false, "اختر باقة صحيحة لإكمال التسجيل.");
            }

            var companyName = NormalizeText(model.CompanyName);
            var tenantId = NormalizeKey(model.TenantId);
            var ownerUsername = NormalizeText(model.OwnerUsername);
            var ownerFullName = NormalizeText(model.OwnerFullName);
            var ownerPhone = NormalizeText(model.OwnerPhoneNumber);
            var ownerEmail = NormalizeText(model.OwnerEmail);
            var password = model.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(companyName))
            {
                return new TenantSignupResult(false, "اسم الجهة أو الموقع مطلوب.");
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                tenantId = NormalizeKey(companyName);
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return new TenantSignupResult(false, "أدخل رابطًا مختصرًا صالحًا للجهة أو الموقع.");
            }

            if (!SaudiNationalIdOrIqamaValidator.IsValid(ownerUsername))
            {
                return new TenantSignupResult(false, SaudiNationalIdOrIqamaValidator.ErrorMessage);
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

            if (db.Tenants.Any(tenant => tenant.TenantId == tenantId || tenant.Slug == tenantId))
            {
                return new TenantSignupResult(false, "الرابط المختصر مستخدم مسبقًا.");
            }

            if (db.UserAccounts.IgnoreQueryFilters().Any(user => user.Username == ownerUsername))
            {
                return new TenantSignupResult(false, "اسم المستخدم مستخدم مسبقًا.");
            }

            db.Tenants.Add(
                new Tenant
                {
                    TenantId = tenantId,
                    Name = companyName,
                    Slug = tenantId,
                    IsActive = false,
                    CreatedAtUtc = now,
                    SubscriptionStatus = TenantSubscriptionStatuses.PendingPayment,
                    SignupExpiresAtUtc = now.AddHours(24),
                    PlanName = plan.Name,
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
                IsActive = false,
                Role = AppRoles.GeneralManager,
                ManagerUsername = string.Empty,
            };
            AppPermissions.ApplyRoleDefaults(owner);
            UserAccountService.SetPassword(owner, password);
            db.UserAccounts.Add(owner);

            db.SaveChanges();
            return new TenantSignupResult(
                true,
                "تم إنشاء الحساب وبانتظار إتمام الدفع.",
                tenantId,
                BuildPaymentReference(tenantId, now),
                _checkoutProtector.Protect(tenantId, TimeSpan.FromMinutes(30))
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

            var plan = TenantPlanCatalog.GetPlans()
                .FirstOrDefault(item => string.Equals(item.Name, tenant.PlanName, StringComparison.Ordinal));

            return new TenantCheckoutViewModel
            {
                TenantId = tenant.TenantId,
                CompanyName = tenant.Name,
                PlanName = tenant.PlanName,
                SubscriptionStatus = tenant.SubscriptionStatus,
                DurationMonths = plan?.DurationMonths ?? 0,
                TotalPrice = plan?.TotalPrice ?? 0,
                PaymentReference = BuildPaymentReference(tenant.TenantId, tenant.CreatedAtUtc),
                LoginUrl = $"/o/{Uri.EscapeDataString(tenant.Slug)}",
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
            var departmentName = NormalizeText(model.DepartmentName);
            var subscriptionStatus = TenantSubscriptionStatuses.Normalize(model.SubscriptionStatus);
            var planName = NormalizeText(model.PlanName);

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
            tenant.SubscriptionStatus = subscriptionStatus;
            tenant.PlanName = string.IsNullOrWhiteSpace(planName)
                ? TenantDefaults.DefaultPlanName
                : planName;
            tenant.TrialEndsAtUtc = NormalizeDate(model.TrialEndsAtUtc);
            tenant.SubscriptionEndsAtUtc = NormalizeDate(model.SubscriptionEndsAtUtc);
            tenant.MaxUsers = NormalizeLimit(model.MaxUsers);
            tenant.MaxPermitsPerMonth = NormalizeLimit(model.MaxPermitsPerMonth);
            tenant.MaxVisitsPerMonth = NormalizeLimit(model.MaxVisitsPerMonth);

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

        public TenantOperationResult DeleteTenant(string tenantId, string confirmationName)
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

            if (!string.Equals(tenant.Name, (confirmationName ?? string.Empty).Trim(), StringComparison.Ordinal))
            {
                return new TenantOperationResult(false, "اسم الجهة المدخل للتأكيد غير مطابق.");
            }

            using var transaction = db.Database.IsRelational()
                ? db.Database.BeginTransaction()
                : null;

            db.PermitActivities.RemoveRange(
                db.PermitActivities.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
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
            db.DisplaySecuritySettings.RemoveRange(
                db.DisplaySecuritySettings.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
            );
            db.ExternalUserLogins.RemoveRange(
                db.ExternalUserLogins.IgnoreQueryFilters().Where(item => item.TenantId == tenant.TenantId)
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
            tenant.SubscriptionEndsAtUtc = DateTime.UtcNow.AddMonths(1);
            tenant.SignupExpiresAtUtc = null;
            if (activatePendingSignupOwner)
            {
                var pendingOwner = db
                    .UserAccounts.IgnoreQueryFilters()
                    .FirstOrDefault(user => user.TenantId == tenant.TenantId);
                if (pendingOwner != null)
                {
                    pendingOwner.IsActive = true;
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
    }
}
