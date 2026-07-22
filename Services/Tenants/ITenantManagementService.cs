using VehiclePermitSystemWeb.Models.ViewModels.Tenants;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public interface ITenantManagementService
    {
        TenantManagementViewModel GetDashboard();
        TenantEditorViewModel? GetEditor(string tenantId);
        TenantOperationResult CreateTenant(TenantEditorViewModel model);
        TenantSignupResult CreateSignup(TenantSignupViewModel model);
        TenantCheckoutViewModel? GetCheckout(string tenantId, string checkoutToken);
        TenantOperationResult UpdateTenant(string tenantId, TenantEditorViewModel model);
        TenantOperationResult SetTenantActive(string tenantId, bool isActive);
        TenantOperationResult DeleteTenant(string tenantId, string confirmationName);
        TenantOperationResult ActivatePaidSubscription(string tenantId);
    }

    public sealed record TenantOperationResult(bool Succeeded, string Message);

    public sealed record TenantSignupResult(
        bool Succeeded,
        string Message,
        string TenantId = "",
        string PaymentReference = "",
        string CheckoutToken = ""
    );
}
