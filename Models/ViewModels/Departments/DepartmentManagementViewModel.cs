using System.ComponentModel.DataAnnotations;
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Models.ViewModels.Departments
{
    public static class DepartmentManagerTransitionTypes
    {
        public const string Permanent = "Permanent";
        public const string Acting = "Acting";
    }

    public static class DepartmentManagerExitActions
    {
        public const string Retirement = "Retirement";
        public const string Resignation = "Resignation";
        public const string EndAssignment = "EndAssignment";
        public const string ExternalTransfer = "ExternalTransfer";
        public const string InternalTransfer = "InternalTransfer";
    }

    public static class GeneralManagerSelectionModes
    {
        public const string ExistingUser = "ExistingUser";
        public const string CreateNew = "CreateNew";
    }

    public static class GeneralManagerAssignmentTypes
    {
        public const string Permanent = "Permanent";
        public const string Acting = "Acting";
    }

    public static class GeneralManagerPreviousActions
    {
        public const string EndAssignment = "EndAssignment";
        public const string InternalTransfer = "InternalTransfer";
        public const string ExternalTransfer = "ExternalTransfer";
        public const string Retirement = "Retirement";
        public const string Resignation = "Resignation";
        public const string Archive = "Archive";
        public const string ReturnToEmployee = "ReturnToEmployee";
    }

    public sealed class GeneralManagerAssignmentRequest
    {
        public string TenantId { get; set; } = string.Empty;

        public int WizardStep { get; set; } = 1;

        public string SelectionMode { get; set; } = GeneralManagerSelectionModes.ExistingUser;

        public string ExistingUserUsername { get; set; } = string.Empty;

        public string NewUserUsername { get; set; } = string.Empty;

        public string NewUserFullName { get; set; } = string.Empty;

        public string NewUserPhoneNumber { get; set; } = string.Empty;

        public string NewUserEmail { get; set; } = string.Empty;

        public string NewUserEmployeeNumber { get; set; } = string.Empty;

        public string NewUserPassword { get; set; } = string.Empty;

        public string NewUserConfirmPassword { get; set; } = string.Empty;

        public string NewUserJobTitle { get; set; } = string.Empty;

        public int NewUserDepartmentId { get; set; }

        public bool NewUserIsActive { get; set; } = true;

        public bool NewUserMustChangePassword { get; set; }

        public string PreviousGeneralManagerAction { get; set; } =
            GeneralManagerPreviousActions.EndAssignment;

        public int PreviousGeneralManagerTargetDepartmentId { get; set; }

        public string AssignmentType { get; set; } = GeneralManagerAssignmentTypes.Permanent;
    }

    public sealed class DepartmentManagerHandoverRequest
    {
        public int DepartmentId { get; set; }

        public int WizardStep { get; set; } = 1;

        public string AssignmentType { get; set; } = DepartmentManagerTransitionTypes.Permanent;

        public bool CreateNewManager { get; set; }

        public string ExistingManagerUsername { get; set; } = string.Empty;

        public string NewManagerUsername { get; set; } = string.Empty;

        public string NewManagerFullName { get; set; } = string.Empty;

        public string NewManagerPhoneNumber { get; set; } = string.Empty;

        public string ExitAction { get; set; } = DepartmentManagerExitActions.EndAssignment;

        public int PreviousManagerTargetDepartmentId { get; set; }

        public string PreviousManagerNewRole { get; set; } = AppRoles.Employee;
    }

    public class DepartmentManagementViewModel
    {
        public Department Department { get; set; } = new Department();

        public Department? AdministrationDepartment { get; set; }

        public string CurrentGeneralManagerUsername { get; set; } = string.Empty;

        public string CurrentGeneralManagerName { get; set; } = string.Empty;

        public string CurrentGeneralManagerJobTitle { get; set; } = string.Empty;

        public string CurrentGeneralManagerPhoneNumber { get; set; } = string.Empty;

        public List<Department> Departments { get; set; } = new List<Department>();

        public Dictionary<int, List<UserAccount>> DepartmentUsersByDepartmentId { get; set; } =
            new();

        public Dictionary<string, string> UserDepartmentsByUsername { get; set; } = new();

        public List<UserAccount> ManagerOptions { get; set; } = new List<UserAccount>();

        public List<UserAccount> HandoverManagerOptions { get; set; } = new List<UserAccount>();

        public List<string> PreviousManagerRoleOptions { get; set; } = new List<string>();

        public int ManagerDepartmentId { get; set; }

        public string ManagerUsername { get; set; } = string.Empty;

        public DepartmentManagerHandoverRequest Handover { get; set; } =
            new DepartmentManagerHandoverRequest();

        public GeneralManagerAssignmentRequest GeneralManagerAssignment { get; set; } =
            new GeneralManagerAssignmentRequest();

        public List<UserAccount> GeneralManagerCandidateOptions { get; set; } =
            new List<UserAccount>();

        public int TransferSourceDepartmentId { get; set; }

        public int TransferTargetDepartmentId { get; set; }

        public bool TransferAllUsers { get; set; }

        public List<string> SelectedTransferUsernames { get; set; } = new List<string>();

        public List<UserAccount> TransferSourceUsers { get; set; } = new List<UserAccount>();

        public List<UserAccount> TransferReplacementOptions { get; set; } = new List<UserAccount>();

        public string TransferReplacementManagerUsername { get; set; } = string.Empty;

        [Display(Name = "رسالة")]
        public string? Message { get; set; }
    }
}
