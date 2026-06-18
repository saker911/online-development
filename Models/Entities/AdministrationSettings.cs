namespace VehiclePermitSystemWeb.Models.Entities
{
    public class AdministrationSettings : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public bool IsInitialSetupCompleted { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string GeneralManagerUsername { get; set; } = string.Empty;
        public string ManagerName { get; set; } = string.Empty;
        public string ManagerTitle { get; set; } = string.Empty;
        public string ManagerPhoneNumber { get; set; } = string.Empty;
        public string SignatureText { get; set; } = string.Empty;
        public string? LogoPath { get; set; }
        public string? SignatureImagePath { get; set; }
        public string DisplayBaseUrl { get; set; } = string.Empty;
        public string DisplayAccessKey { get; set; } = string.Empty;
        public string AllowedClientIpRanges { get; set; } = string.Empty;
        public TimeOnly WorkStartTime { get; set; } = new(8, 0);
        public TimeOnly WorkEndTime { get; set; } = new(16, 0);
        public int AttendanceGraceMinutes { get; set; } = 15;
        public int WorkEndExitGraceMinutes { get; set; } = 30;
        public int LateReturnGraceMinutes { get; set; } = 5;
        public DateTime? LastWorkEndClosureAt { get; set; }

        // Comma-separated list of official work days (e.g. "Sunday,Monday,Tuesday,Wednesday,Thursday")
        public string OfficialWorkDaysCsv { get; set; } =
            "Sunday,Monday,Tuesday,Wednesday,Thursday";
    }
}
