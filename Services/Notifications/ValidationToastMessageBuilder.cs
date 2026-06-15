using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public static class ValidationToastMessageBuilder
    {
        private const string GenericRequiredMessage = "أكمل الحقول الإلزامية المطلوبة.";
        private const string GenericValidationMessage = "تحقق من الحقول المدخلة.";

        public static string? BuildModelStateMessage(ModelStateDictionary? modelState)
        {
            if (modelState == null || modelState.ErrorCount == 0)
            {
                return null;
            }

            var messages = modelState
                .Values.SelectMany(entry => entry.Errors)
                .Select(error => NormalizeMessage(error.ErrorMessage))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return BuildMessage(messages);
        }

        public static string? BuildMessage(IEnumerable<string?> messages)
        {
            var normalizedMessages = messages
                .Select(NormalizeMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (normalizedMessages.Count == 0)
            {
                return null;
            }

            if (normalizedMessages.Count == 1)
            {
                return normalizedMessages[0];
            }

            if (normalizedMessages.All(IsRequiredLikeMessage))
            {
                return GenericRequiredMessage;
            }

            return normalizedMessages.FirstOrDefault(message => !IsRequiredLikeMessage(message))
                ?? GenericValidationMessage;
        }

        private static string NormalizeMessage(string? message)
        {
            return string.IsNullOrWhiteSpace(message) ? GenericValidationMessage : message.Trim();
        }

        private static bool IsRequiredLikeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.Contains("مطلوب", StringComparison.Ordinal)
                || message.Contains("required", StringComparison.OrdinalIgnoreCase)
                || message.Contains("must not be empty", StringComparison.OrdinalIgnoreCase)
                || message.Contains("should not be empty", StringComparison.OrdinalIgnoreCase);
        }
    }
}
