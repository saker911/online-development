using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class InitialPostgreSqlSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdministrationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsInitialSetupCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizationName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DepartmentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    GeneralManagerUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManagerName = table.Column<string>(type: "text", nullable: false),
                    ManagerTitle = table.Column<string>(type: "text", nullable: false),
                    ManagerPhoneNumber = table.Column<string>(type: "text", nullable: false),
                    SignatureText = table.Column<string>(type: "text", nullable: false),
                    LogoPath = table.Column<string>(type: "text", nullable: true),
                    SignatureImagePath = table.Column<string>(type: "text", nullable: true),
                    DisplayBaseUrl = table.Column<string>(type: "text", nullable: false),
                    DisplayAccessKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AllowedClientIpRanges = table.Column<string>(type: "text", nullable: false),
                    WorkStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    WorkEndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    AttendanceGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    WorkEndExitGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    LateReturnGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    LastWorkEndClosureAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OfficialWorkDaysCsv = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministrationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ActualActorUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActedUnderDelegation = table.Column<bool>(type: "boolean", nullable: false),
                    DelegatedFromUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DelegationId = table.Column<int>(type: "integer", nullable: true),
                    BeforeJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AfterJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Delegations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DelegationNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DelegatorUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DelegateeUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastUpdatedByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelledByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CancelReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ManagerUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManagerDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DisplayDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScreenName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ScreenLocation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DeviceTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastIpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DisabledAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisplayDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DisplaySecuritySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SetupKeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequireAdminApproval = table.Column<bool>(type: "boolean", nullable: false),
                    HeartbeatSeconds = table.Column<int>(type: "integer", nullable: false),
                    DeviceCookieDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisplaySecuritySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permits",
                columns: table => new
                {
                    PermitNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PermitType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RequiresReturn = table.Column<bool>(type: "boolean", nullable: false),
                    AccessMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DriverName = table.Column<string>(type: "text", nullable: false),
                    NationalId = table.Column<string>(type: "text", nullable: false),
                    VehicleType = table.Column<string>(type: "text", nullable: false),
                    PlateOrigin = table.Column<string>(type: "text", nullable: false),
                    PlateNumber = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    AuthorizingEntity = table.Column<string>(type: "text", nullable: false),
                    DepartmentName = table.Column<string>(type: "text", nullable: false),
                    VisitLocation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OfficerName = table.Column<string>(type: "text", nullable: false),
                    ManagerName = table.Column<string>(type: "text", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "text", nullable: false),
                    EmployeeDepartment = table.Column<string>(type: "text", nullable: false),
                    JobTitle = table.Column<string>(type: "text", nullable: false),
                    EmployeePhone = table.Column<string>(type: "text", nullable: false),
                    PermitDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExpectedReturnTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LeaveWindowStartAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LeaveWindowEndAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    HasDailyLeaveSchedule = table.Column<bool>(type: "boolean", nullable: false),
                    DailyLeaveScheduleStartDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DailyLeaveScheduleEndDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DailyLeaveScheduleExitMinutes = table.Column<int>(type: "integer", nullable: true),
                    DailyLeaveScheduleReturnMinutes = table.Column<int>(type: "integer", nullable: true),
                    DailyLeaveScheduleRequiresReturn = table.Column<bool>(type: "boolean", nullable: false),
                    DailyLeaveScheduleReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PendingExitRequest = table.Column<bool>(type: "boolean", nullable: false),
                    LeaveReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LateReturnWarningCount = table.Column<int>(type: "integer", nullable: false),
                    LastLateReturnWarningForExpectedReturnTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UnauthorizedExitWarningCount = table.Column<int>(type: "integer", nullable: false),
                    LastUnauthorizedExitWarningAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PendingUnauthorizedExitAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PendingUnauthorizedExitSequenceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastAutomaticWorkEndExitAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OutTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReturnTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "text", nullable: false),
                    CurrentState = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TemporaryExitPermissionUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    QrToken = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permits", x => x.PermitNumber);
                });

            migrationBuilder.CreateTable(
                name: "SessionRecords",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastActivityUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRecords", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    PasswordSalt = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FullName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Department = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmployeeNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperatorBadgeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperatorPinHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OperatorPinSalt = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MustChangeOperatorPin = table.Column<bool>(type: "boolean", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSuperAdmin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CanViewDashboard = table.Column<bool>(type: "boolean", nullable: false),
                    CanViewPermits = table.Column<bool>(type: "boolean", nullable: false),
                    CanViewVisitorPermits = table.Column<bool>(type: "boolean", nullable: false),
                    CanCreatePermit = table.Column<bool>(type: "boolean", nullable: false),
                    CanCreateVisitorPermit = table.Column<bool>(type: "boolean", nullable: false),
                    CanEditPermit = table.Column<bool>(type: "boolean", nullable: false),
                    CanEditVisitorPermit = table.Column<bool>(type: "boolean", nullable: false),
                    CanApprovePermit = table.Column<bool>(type: "boolean", nullable: false),
                    CanApproveLeaveRequest = table.Column<bool>(type: "boolean", nullable: false),
                    CanStopPermit = table.Column<bool>(type: "boolean", nullable: false),
                    CanReviewUnauthorizedExit = table.Column<bool>(type: "boolean", nullable: false),
                    CanViewVisits = table.Column<bool>(type: "boolean", nullable: false),
                    CanCreateVisit = table.Column<bool>(type: "boolean", nullable: false),
                    CanEditVisit = table.Column<bool>(type: "boolean", nullable: false),
                    CanApproveDetainedVisit = table.Column<bool>(type: "boolean", nullable: false),
                    CanApproveVisits = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CanScanOperations = table.Column<bool>(type: "boolean", nullable: false),
                    CanViewDisplays = table.Column<bool>(type: "boolean", nullable: false),
                    CanManageUsers = table.Column<bool>(type: "boolean", nullable: false),
                    CanManageDepartments = table.Column<bool>(type: "boolean", nullable: false),
                    CanManageAdministration = table.Column<bool>(type: "boolean", nullable: false),
                    CanManageDelegations = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ManagerUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Username);
                });

            migrationBuilder.CreateTable(
                name: "UserActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActionLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    VisitId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VisitorName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VisitLocation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HostName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VisitedPersonName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VisitedPersonType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VisitDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EntryTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExitTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VisitApproverUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.VisitId);
                });

            migrationBuilder.CreateTable(
                name: "DelegationPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DelegationId = table.Column<int>(type: "integer", nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegationPermissions_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "Delegations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermitActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PermitNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DriverName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DepartmentName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActionLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReasonCode = table.Column<string>(type: "text", nullable: false),
                    SequenceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassificationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GateName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GateOperatorName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GateOperatorAccount = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExecutionMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsAutomated = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LateMinutes = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitActivities_Permits_PermitNumber",
                        column: x => x.PermitNumber,
                        principalTable: "Permits",
                        principalColumn: "PermitNumber",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VisitCompanions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VisitId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Relationship = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    EntryTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExitTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitCompanions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitCompanions_Visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "Visits",
                        principalColumn: "VisitId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationPermissions_DelegationId_PermissionKey",
                table: "DelegationPermissions",
                columns: new[] { "DelegationId", "PermissionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegateeUsername_Status",
                table: "Delegations",
                columns: new[] { "DelegateeUsername", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegationNumber",
                table: "Delegations",
                column: "DelegationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegatorUsername_Status",
                table: "Delegations",
                columns: new[] { "DelegatorUsername", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisplayDevices_RequestCode",
                table: "DisplayDevices",
                column: "RequestCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitActivities_OccurredAt",
                table: "PermitActivities",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_PermitActivities_PermitNumber_OccurredAt",
                table: "PermitActivities",
                columns: new[] { "PermitNumber", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Permits_ArchivedAt",
                table: "Permits",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_PermitNumber",
                table: "Permits",
                column: "PermitNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitCompanions_VisitId",
                table: "VisitCompanions",
                column: "VisitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministrationSettings");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DelegationPermissions");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "DisplayDevices");

            migrationBuilder.DropTable(
                name: "DisplaySecuritySettings");

            migrationBuilder.DropTable(
                name: "PermitActivities");

            migrationBuilder.DropTable(
                name: "SessionRecords");

            migrationBuilder.DropTable(
                name: "UserAccounts");

            migrationBuilder.DropTable(
                name: "UserActivities");

            migrationBuilder.DropTable(
                name: "VisitCompanions");

            migrationBuilder.DropTable(
                name: "Delegations");

            migrationBuilder.DropTable(
                name: "Permits");

            migrationBuilder.DropTable(
                name: "Visits");
        }
    }
}
