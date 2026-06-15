namespace VehiclePermitSystemWeb.Models.Entities
{
    public class UserActivity
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string RecordedBy { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }

        public string GetActorDisplay()
        {
            var actionType = (ActionType ?? string.Empty).Trim();
            var actorName = string.IsNullOrWhiteSpace(RecordedBy) ? "النظام" : RecordedBy.Trim();
            var targetName = ResolveTargetName();
            var hasTarget =
                !string.IsNullOrWhiteSpace(targetName)
                && !string.Equals(targetName, actorName, StringComparison.OrdinalIgnoreCase);

            if (actionType.Equals("Login", StringComparison.OrdinalIgnoreCase))
            {
                return $"تم تسجيل الدخول بواسطة {actorName}";
            }

            if (actionType.Equals("Logout", StringComparison.OrdinalIgnoreCase))
            {
                return $"تم تسجيل الخروج بواسطة {actorName}";
            }

            if (actionType.Equals("ChangePassword", StringComparison.OrdinalIgnoreCase))
            {
                return $"تم تغيير كلمة المرور بواسطة {actorName}";
            }

            if (actionType.Equals("Approve", StringComparison.OrdinalIgnoreCase))
            {
                return hasTarget
                    ? $"اعتمد من {actorName} لصالح {targetName}"
                    : $"اعتمد من {actorName}";
            }

            if (actionType.Equals("Reject", StringComparison.OrdinalIgnoreCase))
            {
                return hasTarget
                    ? $"رُفض بواسطة {actorName} لصالح {targetName}"
                    : $"رُفض بواسطة {actorName}";
            }

            if (actionType.Equals("ForwardedRequest", StringComparison.OrdinalIgnoreCase))
            {
                return hasTarget
                    ? $"تم رفع الطلب بواسطة {actorName} لصالح {targetName}"
                    : $"تم رفع الطلب بواسطة {actorName}";
            }

            if (actionType.Equals("Create", StringComparison.OrdinalIgnoreCase))
            {
                return hasTarget
                    ? $"تمت الإضافة بواسطة {actorName} على {targetName}"
                    : $"تمت الإضافة بواسطة {actorName}";
            }

            if (actionType.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                return hasTarget
                    ? $"تم التعديل بواسطة {actorName} على {targetName}"
                    : $"تم التعديل بواسطة {actorName}";
            }

            if (actionType.Equals("Activate", StringComparison.OrdinalIgnoreCase))
            {
                return hasTarget
                    ? $"تم التفعيل بواسطة {actorName} على {targetName}"
                    : $"تم التفعيل بواسطة {actorName}";
            }

            if (actionType.Equals("Deactivate", StringComparison.OrdinalIgnoreCase))
            {
                return hasTarget
                    ? $"تم الإيقاف بواسطة {actorName} على {targetName}"
                    : $"تم الإيقاف بواسطة {actorName}";
            }

            return hasTarget ? $"تم بواسطة {actorName} على {targetName}" : $"تم بواسطة {actorName}";
        }

        private string? ResolveTargetName()
        {
            var extractedFromMessage = ExtractTargetFromMessage();
            if (!string.IsNullOrWhiteSpace(extractedFromMessage))
            {
                return extractedFromMessage;
            }

            var displayName = (DisplayName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            var username = (Username ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(username) ? null : username;
        }

        private string? ExtractTargetFromMessage()
        {
            var message = (Message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var actionType = (ActionType ?? string.Empty).Trim();

            if (
                actionType.Equals("Approve", StringComparison.OrdinalIgnoreCase)
                || actionType.Equals("Reject", StringComparison.OrdinalIgnoreCase)
            )
            {
                var subject = ExtractAfterMarker(message, "الخاص بـ ");
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    return subject;
                }

                subject = ExtractAfterMarker(message, "باسم ");
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    return subject;
                }
            }

            if (
                actionType.Equals("Create", StringComparison.OrdinalIgnoreCase)
                || actionType.Equals("Update", StringComparison.OrdinalIgnoreCase)
                || actionType.Equals("Activate", StringComparison.OrdinalIgnoreCase)
                || actionType.Equals("Deactivate", StringComparison.OrdinalIgnoreCase)
            )
            {
                var subject = ExtractAfterMarker(
                    message,
                    "المستخدم ",
                    " بالدور",
                    " والصلاحيات",
                    " بنجاح",
                    "."
                );
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    return subject;
                }

                subject = ExtractAfterMarker(message, "الزيارة رقم ", " باسم", ".");
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    return subject;
                }
            }

            return ExtractAfterMarker(message, "باسم ") ?? ExtractAfterMarker(message, "الخاص بـ ");
        }

        private static string? ExtractAfterMarker(
            string value,
            string marker,
            params string[] stopTokens
        )
        {
            var startIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
            {
                return null;
            }

            var subject = value[(startIndex + marker.Length)..].Trim();
            if (stopTokens.Length > 0)
            {
                var cutoff = subject.Length;
                foreach (var token in stopTokens)
                {
                    var tokenIndex = subject.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                    if (tokenIndex >= 0 && tokenIndex < cutoff)
                    {
                        cutoff = tokenIndex;
                    }
                }

                subject = subject[..cutoff].Trim();
            }

            return subject.TrimEnd('.', '،', ',', ';', ':', '!', ')', '(', ']', '[');
        }
    }
}
