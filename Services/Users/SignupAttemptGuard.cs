using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Services.Users
{
    public sealed class SignupAttemptGuard
    {
        private const int MaxSuccessfulSignups = 3;
        private static readonly TimeSpan TrackingWindow = TimeSpan.FromHours(1);
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ConcurrentDictionary<string, object> _keyLocks = new();

        public SignupAttemptGuard(IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public bool CanCreate(string remoteIp, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;
            var keyHash = BuildKeyHash(remoteIp);
            lock (_keyLocks.GetOrAdd(keyHash, _ => new object()))
            {
                using var db = _dbContextFactory.CreateDbContext();
                var record = db.SignupAttemptRecords.SingleOrDefault(item => item.KeyHash == keyHash);
                if (record == null)
                {
                    return true;
                }

                var now = DateTime.UtcNow;
                var windowEndsAt = record.WindowStartedAtUtc.Add(TrackingWindow);
                if (windowEndsAt <= now)
                {
                    db.SignupAttemptRecords.Remove(record);
                    db.SaveChanges();
                    return true;
                }

                if (record.SuccessCount < MaxSuccessfulSignups)
                {
                    return true;
                }

                retryAfter = windowEndsAt - now;
                return false;
            }
        }

        public void RecordSuccess(string remoteIp)
        {
            var keyHash = BuildKeyHash(remoteIp);
            var now = DateTime.UtcNow;
            lock (_keyLocks.GetOrAdd(keyHash, _ => new object()))
            {
                using var db = _dbContextFactory.CreateDbContext();
                var record = db.SignupAttemptRecords.SingleOrDefault(item => item.KeyHash == keyHash);
                if (record == null)
                {
                    record = new SignupAttemptRecord
                    {
                        KeyHash = keyHash,
                        WindowStartedAtUtc = now,
                    };
                    db.SignupAttemptRecords.Add(record);
                }
                else if (record.WindowStartedAtUtc.Add(TrackingWindow) <= now)
                {
                    record.SuccessCount = 0;
                    record.WindowStartedAtUtc = now;
                }

                record.SuccessCount++;
                record.UpdatedAtUtc = now;
                db.SaveChanges();
            }
        }

        private static string BuildKeyHash(string remoteIp)
        {
            var normalized = $"signup|{(remoteIp ?? string.Empty).Trim()}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        }
    }
}
