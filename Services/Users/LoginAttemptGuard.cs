using System.Collections.Concurrent;

namespace VehiclePermitSystemWeb.Services.Users
{
    public sealed class LoginAttemptGuard
    {
        private const int MaxFailures = 5;
        private static readonly TimeSpan TrackingWindow = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
        private readonly ConcurrentDictionary<string, AttemptState> _attempts =
            new(StringComparer.OrdinalIgnoreCase);

        public bool IsBlocked(
            string username,
            string tenantId,
            string remoteIp,
            out TimeSpan retryAfter
        )
        {
            retryAfter = TimeSpan.Zero;
            var key = BuildKey(username, tenantId, remoteIp);
            if (!_attempts.TryGetValue(key, out var state))
            {
                return false;
            }

            lock (state.SyncRoot)
            {
                var now = DateTime.UtcNow;
                if (state.BlockedUntilUtc > now)
                {
                    retryAfter = state.BlockedUntilUtc - now;
                    return true;
                }

                if (now - state.WindowStartedUtc > TrackingWindow)
                {
                    _attempts.TryRemove(key, out _);
                }

                return false;
            }
        }

        public void RecordFailure(string username, string tenantId, string remoteIp)
        {
            var key = BuildKey(username, tenantId, remoteIp);
            var now = DateTime.UtcNow;
            var state = _attempts.GetOrAdd(
                key,
                _ => new AttemptState { WindowStartedUtc = now }
            );

            lock (state.SyncRoot)
            {
                if (now - state.WindowStartedUtc > TrackingWindow)
                {
                    state.WindowStartedUtc = now;
                    state.FailureCount = 0;
                    state.BlockedUntilUtc = DateTime.MinValue;
                }

                state.FailureCount++;
                if (state.FailureCount >= MaxFailures)
                {
                    state.BlockedUntilUtc = now.Add(LockoutDuration);
                }
            }
        }

        public void Reset(string username, string tenantId, string remoteIp)
        {
            _attempts.TryRemove(BuildKey(username, tenantId, remoteIp), out _);
        }

        private static string BuildKey(string username, string tenantId, string remoteIp) =>
            $"{(tenantId ?? string.Empty).Trim()}|{(username ?? string.Empty).Trim()}|{(remoteIp ?? string.Empty).Trim()}";

        private sealed class AttemptState
        {
            public object SyncRoot { get; } = new();
            public int FailureCount { get; set; }
            public DateTime WindowStartedUtc { get; set; }
            public DateTime BlockedUntilUtc { get; set; }
        }
    }
}
