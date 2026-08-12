namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class PersonProfile : ITenantScopedEntity
    {
        public long Id { get; set; }
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string SourceKey { get; set; } = string.Empty;
        public string PersonType { get; set; } = PersonTypes.Employee;
        public string FullName { get; set; } = string.Empty;
        public string NationalId { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public string LastReference { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime? LastSeenAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public static class PersonTypes
    {
        public const string Employee = "Employee";
        public const string Visitor = "Visitor";
        public const string Contractor = "Contractor";

        public static string DisplayName(string? personType) =>
            personType switch
            {
                Visitor => "زائر",
                Contractor => "متعاقد",
                _ => "موظف",
            };
    }
}
