using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Services.Users
{
    public sealed record ExternalIdentity(
        string Provider,
        string Issuer,
        string Subject,
        string Email
    );

    public sealed record ExternalLoginLinkResult(bool Succeeded, string Message);

    public interface IExternalLoginService
    {
        ExternalIdentity? ReadIdentity(ClaimsPrincipal principal, string provider);
        ExternalUserLogin? FindLogin(ExternalIdentity identity, string? tenantId = null);
        IReadOnlySet<string> GetLinkedProviders(string tenantId, string username);
        ExternalLoginLinkResult Link(
            ExternalIdentity identity,
            string tenantId,
            string username
        );
        ExternalLoginLinkResult Unlink(string provider, string tenantId, string username);
    }

    public sealed class ExternalLoginService : IExternalLoginService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public ExternalLoginService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public ExternalIdentity? ReadIdentity(ClaimsPrincipal principal, string provider)
        {
            var normalizedProvider = ExternalAuthenticationDefaults.NormalizeProvider(provider);
            if (normalizedProvider == null)
            {
                return null;
            }

            var issuer = principal.FindFirstValue("iss")?.Trim() ?? string.Empty;
            var subject = principal.FindFirstValue("sub")?.Trim()
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)?.Trim()
                ?? string.Empty;

            if (
                string.Equals(
                    normalizedProvider,
                    ExternalAuthenticationDefaults.GoogleScheme,
                    StringComparison.Ordinal
                )
            )
            {
                issuer = "https://accounts.google.com";
            }

            if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            {
                return null;
            }

            var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim() ?? string.Empty;
            return new ExternalIdentity(normalizedProvider, issuer, subject, email);
        }

        public ExternalUserLogin? FindLogin(ExternalIdentity identity, string? tenantId = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var query = db
                .ExternalUserLogins.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(login =>
                    login.Provider == identity.Provider
                    && login.Issuer == identity.Issuer
                    && login.Subject == identity.Subject
                );

            var normalizedTenantId = (tenantId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedTenantId))
            {
                query = query.Where(login => login.TenantId == normalizedTenantId);
            }

            return query.SingleOrDefault();
        }

        public IReadOnlySet<string> GetLinkedProviders(string tenantId, string username)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return db
                .ExternalUserLogins.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(login => login.TenantId == tenantId && login.Username == username)
                .Select(login => login.Provider)
                .ToHashSet(StringComparer.Ordinal);
        }

        public ExternalLoginLinkResult Link(
            ExternalIdentity identity,
            string tenantId,
            string username
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var existingIdentity = db
                .ExternalUserLogins.IgnoreQueryFilters()
                .SingleOrDefault(login =>
                    login.Provider == identity.Provider
                    && login.Issuer == identity.Issuer
                    && login.Subject == identity.Subject
                );
            if (existingIdentity != null)
            {
                var belongsToCurrentUser =
                    string.Equals(existingIdentity.TenantId, tenantId, StringComparison.Ordinal)
                    && string.Equals(existingIdentity.Username, username, StringComparison.Ordinal);
                return belongsToCurrentUser
                    ? new ExternalLoginLinkResult(true, "الحساب الخارجي مرتبط مسبقًا.")
                    : new ExternalLoginLinkResult(false, "الحساب الخارجي مرتبط بحساب آخر.");
            }

            var existingProvider = db
                .ExternalUserLogins.IgnoreQueryFilters()
                .SingleOrDefault(login =>
                    login.TenantId == tenantId
                    && login.Username == username
                    && login.Provider == identity.Provider
                );
            if (existingProvider != null)
            {
                return new ExternalLoginLinkResult(
                    false,
                    "أزل الربط الحالي مع المزود قبل ربط حساب مختلف."
                );
            }

            db.ExternalUserLogins.Add(
                new ExternalUserLogin
                {
                    TenantId = tenantId,
                    Username = username,
                    Provider = identity.Provider,
                    Issuer = identity.Issuer,
                    Subject = identity.Subject,
                    EmailAtLinkTime = identity.Email,
                    LinkedAtUtc = DateTime.UtcNow,
                }
            );
            try
            {
                db.SaveChanges();
            }
            catch (DbUpdateException)
            {
                return new ExternalLoginLinkResult(
                    false,
                    "تعذر حفظ الربط لأن الحساب الخارجي مرتبط مسبقًا."
                );
            }
            return new ExternalLoginLinkResult(true, "تم ربط الحساب الخارجي بأمان.");
        }

        public ExternalLoginLinkResult Unlink(string provider, string tenantId, string username)
        {
            var normalizedProvider = ExternalAuthenticationDefaults.NormalizeProvider(provider);
            if (normalizedProvider == null)
            {
                return new ExternalLoginLinkResult(false, "مزود الهوية غير صالح.");
            }

            using var db = _dbContextFactory.CreateDbContext();
            var login = db
                .ExternalUserLogins.IgnoreQueryFilters()
                .SingleOrDefault(item =>
                    item.TenantId == tenantId
                    && item.Username == username
                    && item.Provider == normalizedProvider
                );
            if (login == null)
            {
                return new ExternalLoginLinkResult(false, "لا يوجد ربط محفوظ لهذا المزود.");
            }

            db.ExternalUserLogins.Remove(login);
            db.SaveChanges();
            return new ExternalLoginLinkResult(true, "تم إلغاء ربط الحساب الخارجي.");
        }
    }
}
