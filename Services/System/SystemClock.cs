namespace VehiclePermitSystemWeb.Services.Common
{
    public sealed class SystemClock : ISystemClock
    {
        public DateTime LocalNow => DateTime.Now;

        public DateTime UtcNow => DateTime.UtcNow;
    }
}
