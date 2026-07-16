using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Services.Users
{
    public sealed class LoginAttemptGuard
    {
        private const int MaxFailures = 5;
        private static readonly TimeSpan TrackingWindow = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
        private readonly IDbContextFactory<ApplicationDbContext>? _dbContextFactory;
        private readonly ConcurrentDictionary<string, AttemptState> _fallbackAttempts = new();
        private readonly ConcurrentDictionary<string, object> _keyLocks = new();

        public LoginAttemptGuard(IDbContextFactory<ApplicationDbContext>? dbContextFactory = null)
        {
            _dbContextFactory = dbContextFactory;
        }

        public bool IsBlocked(
            string username,
            string tenantId,
            string remoteIp,
            out TimeSpan retryAfter
        )
        {
            retryAfter = TimeSpan.Zero;
            var keyHash = BuildKeyHash(username, tenantId, remoteIp);
            if (_dbContextFactory == null)
            {
                return IsFallbackBlocked(keyHash, out retryAfter);
            }

            lock (_keyLocks.GetOrAdd(keyHash, _ => new object()))
            {
                using var db = _dbContextFactory.CreateDbContext();
                var record = db.LoginAttemptRecords.SingleOrDefault(item => item.KeyHash == keyHash);
                if (record == null)
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                if (record.BlockedUntilUtc > now)
                {
                    retryAfter = record.BlockedUntilUtc.Value - now;
                    return true;
                }

                if (now - record.WindowStartedAtUtc > TrackingWindow)
                {
                    db.LoginAttemptRecords.Remove(record);
                    db.SaveChanges();
                }

                return false;
            }
        }

        public void RecordFailure(string username, string tenantId, string remoteIp)
        {
            var keyHash = BuildKeyHash(username, tenantId, remoteIp);
            var now = DateTime.UtcNow;
            if (_dbContextFactory == null)
            {
                RecordFallbackFailure(keyHash, now);
                return;
            }

            lock (_keyLocks.GetOrAdd(keyHash, _ => new object()))
            {
                using var db = _dbContextFactory.CreateDbContext();
                var record = db.LoginAttemptRecords.SingleOrDefault(item => item.KeyHash == keyHash);
                if (record == null)
                {
                    record = new LoginAttemptRecord
                    {
                        KeyHash = keyHash,
                        FailureCount = 0,
                        WindowStartedAtUtc = now,
                    };
                    db.LoginAttemptRecords.Add(record);
                }
                else if (now - record.WindowStartedAtUtc > TrackingWindow)
                {
                    record.FailureCount = 0;
                    record.WindowStartedAtUtc = now;
                    record.BlockedUntilUtc = null;
                }

                record.FailureCount++;
                record.UpdatedAtUtc = now;
                if (record.FailureCount >= MaxFailures)
                {
                    record.BlockedUntilUtc = now.Add(LockoutDuration);
                }

                db.SaveChanges();
            }
        }

        public void Reset(string username, string tenantId, string remoteIp)
        {
            var keyHash = BuildKeyHash(username, tenantId, remoteIp);
            _fallbackAttempts.TryRemove(keyHash, out _);
            if (_dbContextFactory == null)
            {
                return;
            }

            lock (_keyLocks.GetOrAdd(keyHash, _ => new object()))
            {
                using var db = _dbContextFactory.CreateDbContext();
                var record = db.LoginAttemptRecords.SingleOrDefault(item => item.KeyHash == keyHash);
                if (record != null)
                {
                    db.LoginAttemptRecords.Remove(record);
                    db.SaveChanges();
                }
            }
        }

        private bool IsFallbackBlocked(string keyHash, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;
            if (!_fallbackAttempts.TryGetValue(keyHash, out var state))
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
                    _fallbackAttempts.TryRemove(keyHash, out _);
                }

                return false;
            }
        }

        private void RecordFallbackFailure(string keyHash, DateTime now)
        {
            var state = _fallbackAttempts.GetOrAdd(
                keyHash,
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

        private static string BuildKeyHash(string username, string tenantId, string remoteIp)
        {
            var normalized = string.Join(
                '|',
                (tenantId ?? string.Empty).Trim().ToLowerInvariant(),
                (username ?? string.Empty).Trim().ToLowerInvariant(),
                (remoteIp ?? string.Empty).Trim()
            );
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        }

        private sealed class AttemptState
        {
            public object SyncRoot { get; } = new();
            public int FailureCount { get; set; }
            public DateTime WindowStartedUtc { get; set; }
            public DateTime BlockedUntilUtc { get; set; }
        }
    }
}
