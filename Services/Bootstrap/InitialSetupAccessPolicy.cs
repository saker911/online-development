using System.ComponentModel.DataAnnotations;
using VehiclePermitSystemWeb.Models.ViewModels.Account;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Utilities.Security;

namespace VehiclePermitSystemWeb.Services.Bootstrap;

public interface IInitialSetupAccessPolicy
{
    bool IsWebSetupAllowed { get; }
}

public sealed class InitialSetupAccessPolicy : IInitialSetupAccessPolicy
{
    public InitialSetupAccessPolicy(IHostEnvironment environment, IConfiguration configuration)
    {
        IsWebSetupAllowed = environment.IsDevelopment()
            && configuration.GetValue("Bootstrap:AllowWebInitialSetup", true);
    }

    public bool IsWebSetupAllowed { get; }
}

public static class ServerInitialSetupCommand
{
    public const string CommandArgument = "--bootstrap-owner";

    public static int Execute(
        IUserAdminService userAdminService,
        IConfiguration configuration,
        ILogger logger
    )
    {
        if (!userAdminService.IsInitialSetupRequired())
        {
            logger.LogInformation("Initial setup is already complete; no bootstrap changes were made.");
            return 0;
        }

        var model = new InitialSetupViewModel
        {
            Username = Read(configuration, "OwnerUsername"),
            FullName = Read(configuration, "OwnerFullName"),
            PhoneNumber = Read(configuration, "OwnerPhone"),
            JobTitle = Read(configuration, "OwnerJobTitle", "مالك النظام"),
            Password = Read(configuration, "OwnerPassword"),
            OrganizationName = Read(configuration, "OrganizationName"),
            AdministrationPhone = Read(configuration, "OrganizationPhone"),
            AdministrationEmail = Read(configuration, "OrganizationEmail"),
            AdministrationAddress = Read(configuration, "OrganizationAddress"),
        };
        model.ConfirmPassword = model.Password;

        var invalidFields = Validate(model);
        if (invalidFields.Count > 0)
        {
            logger.LogError(
                "Server bootstrap was rejected. Missing or invalid configuration fields: {Fields}",
                string.Join(", ", invalidFields)
            );
            return 2;
        }

        if (!userAdminService.CompleteInitialSetup(model))
        {
            logger.LogError("Server bootstrap could not create the initial owner account.");
            return 3;
        }

        userAdminService.RecordUserActivity(
            model.Username,
            model.FullName,
            "InitialSetup",
            "تهيئة آمنة من الخادم",
            "تم إنشاء حساب مالك النظام الأول باستخدام أمر الخادم.",
            nameof(ServerInitialSetupCommand),
            "server-bootstrap"
        );
        logger.LogInformation("Initial owner account was created successfully from the server command.");
        return 0;
    }

    private static List<string> Validate(InitialSetupViewModel model)
    {
        var invalid = new List<string>();
        if (!AccountUsernameValidator.IsValid(model.Username))
            invalid.Add("Bootstrap__OwnerUsername");
        if (string.IsNullOrWhiteSpace(model.FullName))
            invalid.Add("Bootstrap__OwnerFullName");
        if (!SaudiMobileNumberValidator.IsValidRequired(model.PhoneNumber))
            invalid.Add("Bootstrap__OwnerPhone");
        if (!PasswordValidationRules.IsStrongPassword(model.Password))
            invalid.Add("Bootstrap__OwnerPassword");
        if (string.IsNullOrWhiteSpace(model.OrganizationName))
            invalid.Add("Bootstrap__OrganizationName");
        if (!SaudiLandlineNumberValidator.IsValidRequired(model.AdministrationPhone))
            invalid.Add("Bootstrap__OrganizationPhone");
        if (!new EmailAddressAttribute().IsValid(model.AdministrationEmail))
            invalid.Add("Bootstrap__OrganizationEmail");
        if (string.IsNullOrWhiteSpace(model.AdministrationAddress))
            invalid.Add("Bootstrap__OrganizationAddress");
        return invalid;
    }

    private static string Read(
        IConfiguration configuration,
        string key,
        string defaultValue = ""
    ) => (configuration[$"Bootstrap:{key}"] ?? defaultValue).Trim();
}
