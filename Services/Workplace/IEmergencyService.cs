using VehiclePermitSystemWeb.Models.ViewModels.Workplace;

namespace VehiclePermitSystemWeb.Services.Workplace
{
    public interface IEmergencyService
    {
        EmergencyDashboardViewModel BuildDashboard(int? siteId);
        bool StartSession(int siteId, string actor, out string error);
        bool UpdateMember(int sessionId, int memberId, string status, string actor, out string error);
        bool CompleteSession(int sessionId, string actor, out string error);
    }
}
