namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class EmergencySession : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public int WorkplaceSiteId { get; set; }
        public string Status { get; set; } = EmergencySessionStatuses.Active;
        public string StartedBy { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public string EndedBy { get; set; } = string.Empty;
        public DateTime? EndedAtUtc { get; set; }
        public WorkplaceSite? WorkplaceSite { get; set; }
        public ICollection<EmergencySessionMember> Members { get; set; } =
            new List<EmergencySessionMember>();
    }

    public sealed class EmergencySessionMember : ITenantScopedEntity
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public int EmergencySessionId { get; set; }
        public long PersonProfileId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string PersonType { get; set; } = PersonTypes.Employee;
        public string Reference { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = EmergencyMemberStatuses.Pending;
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime? UpdatedAtUtc { get; set; }
        public EmergencySession? EmergencySession { get; set; }
    }

    public static class EmergencySessionStatuses
    {
        public const string Active = "Active";
        public const string Completed = "Completed";
    }

    public static class EmergencyMemberStatuses
    {
        public const string Pending = "Pending";
        public const string Safe = "Safe";
        public const string Assistance = "Assistance";
        public const string Missing = "Missing";

        public static IReadOnlyList<string> Supported { get; } =
            new[] { Pending, Safe, Assistance, Missing };

        public static string DisplayName(string? status) => status switch
        {
            Safe => "آمن",
            Assistance => "يحتاج مساعدة",
            Missing => "غير موجود",
            _ => "بانتظار التحقق",
        };
    }
}
