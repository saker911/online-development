using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Utilities.Users
{
    public static class UserCredentialsMapper
    {
        public static bool ChangePassword(
            ApplicationDbContext db,
            string username,
            string currentPassword,
            string newPassword
        )
        {
            return UserAccountService.ChangePassword(db, username, currentPassword, newPassword);
        }

        public static string EnsureOperatorBadgeCode(
            ApplicationDbContext db,
            string username,
            string? preferredBadgeCode = null,
            bool ignoreTenantFilters = false
        )
        {
            return UserAccountService.EnsureOperatorBadgeCode(
                db,
                username,
                preferredBadgeCode,
                ignoreTenantFilters
            );
        }

        public static string? ConfigureOperatorCredentials(
            ApplicationDbContext db,
            string username,
            string badgeCode,
            string? temporaryPin,
            bool requirePinChange,
            bool ignoreTenantFilters = false
        )
        {
            return UserAccountService.ConfigureOperatorCredentials(
                db,
                username,
                badgeCode,
                temporaryPin,
                requirePinChange,
                ignoreTenantFilters
            );
        }
    }
}
