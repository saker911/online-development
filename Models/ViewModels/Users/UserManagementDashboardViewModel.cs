namespace VehiclePermitSystemWeb.Models.ViewModels.Users
{
    public class UserManagementDashboardViewModel
    {
        public List<UserAccount> Users { get; set; } = new();
        public List<UserActivity> RecentActivities { get; set; } = new();
        public Dictionary<string, int> RoleDistribution { get; set; } = new();
        public Dictionary<string, int> PermissionDistribution { get; set; } = new();
        public Dictionary<string, string> ManagedDepartmentNamesByUsername { get; set; } = new();
        public Dictionary<string, int> ManagedDepartmentIdsByUsername { get; set; } = new();
    }
}
