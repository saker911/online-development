using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public sealed class ToastNotificationService : IToastNotificationService
    {
        private const string TempDataKey = "__ToastNotifications";

        private static readonly JsonSerializerOptions SerializerOptions = new(
            JsonSerializerDefaults.Web
        );

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITempDataDictionaryFactory _tempDataDictionaryFactory;

        public ToastNotificationService(
            IHttpContextAccessor httpContextAccessor,
            ITempDataDictionaryFactory tempDataDictionaryFactory
        )
        {
            _httpContextAccessor = httpContextAccessor;
            _tempDataDictionaryFactory = tempDataDictionaryFactory;
        }

        public void Success(string? message)
        {
            Import(message, "success");
        }

        public void Error(string? message)
        {
            Import(message, "danger");
        }

        public void Warning(string? message)
        {
            Import(message, "warning");
        }

        public void Info(string? message)
        {
            Import(message, "info");
        }

        public void Import(string? message, string type = "info")
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var tempData = GetTempData();
            if (tempData == null)
            {
                return;
            }

            var notifications = ReadNotifications(tempData);
            var normalizedMessage = message.Trim();
            var normalizedType = NormalizeType(type);

            if (
                notifications.Any(notification =>
                    string.Equals(notification.Message, normalizedMessage, StringComparison.Ordinal)
                    && string.Equals(
                        notification.Type,
                        normalizedType,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                WriteNotifications(tempData, notifications);
                return;
            }

            notifications.Add(
                new ToastNotificationItem(
                    Guid.NewGuid().ToString("N"),
                    normalizedMessage,
                    normalizedType
                )
            );
            WriteNotifications(tempData, notifications);
        }

        public void Import(IEnumerable<ToastNotificationItem> notifications)
        {
            foreach (var notification in notifications)
            {
                Import(notification.Message, notification.Type);
            }
        }

        public void ImportModelState(ModelStateDictionary modelState)
        {
            Error(ValidationToastMessageBuilder.BuildModelStateMessage(modelState));
        }

        public IReadOnlyList<ToastNotificationItem> ConsumeNotifications()
        {
            var tempData = GetTempData();
            if (tempData == null)
            {
                return Array.Empty<ToastNotificationItem>();
            }

            var notifications = ReadNotifications(tempData);
            tempData.Remove(TempDataKey);
            return notifications;
        }

        private ITempDataDictionary? GetTempData()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return null;
            }

            return _tempDataDictionaryFactory.GetTempData(httpContext);
        }

        private static List<ToastNotificationItem> ReadNotifications(ITempDataDictionary tempData)
        {
            if (tempData.TryGetValue(TempDataKey, out var rawValue) && rawValue is string payload)
            {
                try
                {
                    return JsonSerializer.Deserialize<List<ToastNotificationItem>>(
                            payload,
                            SerializerOptions
                        ) ?? new List<ToastNotificationItem>();
                }
                catch (JsonException)
                {
                    return new List<ToastNotificationItem>();
                }
            }

            return new List<ToastNotificationItem>();
        }

        private static void WriteNotifications(
            ITempDataDictionary tempData,
            List<ToastNotificationItem> notifications
        )
        {
            tempData[TempDataKey] = JsonSerializer.Serialize(notifications, SerializerOptions);
        }

        private static string NormalizeType(string? type)
        {
            return type?.Trim().ToLowerInvariant() switch
            {
                "success" => "success",
                "danger" => "danger",
                "error" => "danger",
                "warning" => "warning",
                _ => "info",
            };
        }
    }
}
