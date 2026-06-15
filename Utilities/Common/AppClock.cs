using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Utilities.Common
{
    public static class AppClock
    {
        private static Func<DateTime> _localNow = static () => DateTime.Now;
        private static Func<DateTime> _utcNow = static () => DateTime.UtcNow;

        public static DateTime LocalNow => _localNow();

        public static DateTime UtcNow => _utcNow();

        public static void Configure(ISystemClock clock)
        {
            ArgumentNullException.ThrowIfNull(clock);
            _localNow = () => clock.LocalNow;
            _utcNow = () => clock.UtcNow;
        }

        public static IDisposable Override(Func<DateTime> localNow, Func<DateTime>? utcNow = null)
        {
            ArgumentNullException.ThrowIfNull(localNow);

            var previousLocalNow = _localNow;
            var previousUtcNow = _utcNow;

            _localNow = localNow;
            _utcNow = utcNow ?? localNow;

            return new RestoreClockScope(previousLocalNow, previousUtcNow);
        }

        private sealed class RestoreClockScope : IDisposable
        {
            private readonly Func<DateTime> _previousLocalNow;
            private readonly Func<DateTime> _previousUtcNow;
            private bool _disposed;

            public RestoreClockScope(Func<DateTime> previousLocalNow, Func<DateTime> previousUtcNow)
            {
                _previousLocalNow = previousLocalNow;
                _previousUtcNow = previousUtcNow;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _localNow = _previousLocalNow;
                _utcNow = _previousUtcNow;
                _disposed = true;
            }
        }
    }
}
