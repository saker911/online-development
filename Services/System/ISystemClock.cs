namespace VehiclePermitSystemWeb.Services.Common
{
    public interface ISystemClock
    {
        DateTime LocalNow { get; }

        DateTime UtcNow { get; }
    }
}
