namespace VehiclePermitSystemWeb.Models.Entities
{
    public class SessionRecord
    {
        public string SessionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime LastActivityUtc { get; set; }
    }
}
