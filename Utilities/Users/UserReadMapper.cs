using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Account;
using VehiclePermitSystemWeb.Models.ViewModels.Backup;
using VehiclePermitSystemWeb.Models.ViewModels.Delegations;
using VehiclePermitSystemWeb.Models.ViewModels.Departments;
using VehiclePermitSystemWeb.Models.ViewModels.Display;
using VehiclePermitSystemWeb.Models.ViewModels.Permits;
using VehiclePermitSystemWeb.Models.ViewModels.Reports;
using VehiclePermitSystemWeb.Models.ViewModels.Scan;
using VehiclePermitSystemWeb.Models.ViewModels.Users;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;

namespace VehiclePermitSystemWeb.Utilities.Users
{
    public static class UserReadMapper
    {
        public static IEnumerable<UserAccount> GetAllUsers(ApplicationDbContext db)
        {
            return db.UserAccounts.AsNoTracking().OrderBy(user => user.DisplayName).ToList();
        }

        public static UserAccount? GetUserAccount(ApplicationDbContext db, string username)
        {
            return db.UserAccounts.AsNoTracking().FirstOrDefault(user => user.Username == username);
        }

        public static bool HasAnyUsers(ApplicationDbContext db)
        {
            return db.UserAccounts.AsNoTracking().Any();
        }
    }
}
