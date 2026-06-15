namespace PermitBehaviorChecks;

internal sealed class MutableSystemClock : ISystemClock
{
    public MutableSystemClock(DateTime initialLocalNow)
    {
        LocalNow = initialLocalNow;
    }

    public DateTime LocalNow { get; private set; }

    public DateTime UtcNow => LocalNow.ToUniversalTime();

    public void SetLocalNow(DateTime value)
    {
        LocalNow = value;
    }

    public void Advance(TimeSpan duration)
    {
        LocalNow = LocalNow.Add(duration);
    }
}
