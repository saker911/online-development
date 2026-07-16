namespace VehiclePermitSystemWeb.Models.ViewModels.Account
{
    public class AccountProfileViewModel
    {
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string RoleDisplayName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string ManagerUsername { get; set; } = string.Empty;
        public string ManagerDisplayName { get; set; } = string.Empty;
        public bool MustChangePassword { get; set; }
        public bool MustChangeOperatorPin { get; set; }
        public List<string> PermissionDisplayNames { get; set; } = new();
        public HashSet<string> GrantedPermissionKeys { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public bool CanScanOperations { get; set; }
        public string OperatorBadgeCode { get; set; } = string.Empty;
        public HashSet<string> LinkedExternalProviders { get; set; } =
            new(StringComparer.Ordinal);
    }
}
