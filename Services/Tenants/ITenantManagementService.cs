using VehiclePermitSystemWeb.Models.ViewModels.Tenants;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public interface ITenantManagementService
    {
        TenantManagementViewModel GetDashboard();
        TenantEditorViewModel? GetEditor(string tenantId);
        TenantOperationResult CreateTenant(TenantEditorViewModel model);
        TenantSignupResult CreateSignup(
            TenantSignupViewModel model,
            bool emailConfirmed = false
        );
        TenantSignupResult CreateGoogleTrial(string fullName, string email);
        TenantCheckoutViewModel? GetCheckout(string tenantId, string checkoutToken);
        TenantOperationResult UpdateTenant(string tenantId, TenantEditorViewModel model);
        TenantOperationResult SetTenantActive(string tenantId, bool isActive);
        TenantOperationResult DeleteTenant(
            string tenantId,
            string deletionReason,
            bool permanentDeletionConfirmed
        );
        TenantOperationResult ActivatePaidSubscription(string tenantId);
    }

    public sealed record TenantOperationResult(
        bool Succeeded,
        string Message,
        string TenantId = "",
        string OwnerUsername = ""
    );

    public sealed record TenantSignupResult(
        bool Succeeded,
        string Message,
        string TenantId = "",
        string PaymentReference = "",
        string CheckoutToken = "",
        string OwnerUsername = ""
    );
}
