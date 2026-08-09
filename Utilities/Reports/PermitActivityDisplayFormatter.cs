namespace VehiclePermitSystemWeb.Utilities.Reports
{
    public static class PermitActivityDisplayFormatter
    {
        public static string Classification(string? value) => Normalize(value) switch
        {
            "pending" => "معلّق",
            "closedwithoutviolation" => "أغلقت دون مخالفة",
            "confirmedviolation" => "مخالفة مؤكدة",
            "needsadministrativereview" => "تحتاج مراجعة",
            "completed" => "مكتمل",
            "stopped" => "موقوف",
            "system" => "نظامي",
            _ => Fallback(value, "غير مصنف"),
        };

        public static string ReasonCode(string? value) => Normalize(value) switch
        {
            "securityviolation" => "مخالفة أمنية",
            "noreturnviolation" => "عدم العودة",
            "latereturn" => "عودة متأخرة",
            "lateattendance" => "تأخر حضور",
            "latecheckout" => "تأخر انصراف",
            "overrideentry" => "دخول استثنائي",
            "entry" => "دخول",
            "return" => "عودة",
            "exitauthorized" => "خروج بإذن",
            "exitfinal" => "خروج نهائي",
            "exitunauthorized" => "خروج بدون إذن",
            "pendingunauthorizedexit" => "محاولة معلقة",
            "deniedattemptclosed" => "أغلقت دون مخالفة",
            "unauthorizedexitconfirmed" => "مخالفة مؤكدة",
            "returnafterunauthorizedexit" => "عودة بعد خروج غير مصرح",
            "unauthorizedexitneedsreview" => "مراجعة إدارية",
            "administrativereviewconfirmed" => "اعتماد إداري للمخالفة",
            "administrativereviewdismissed" => "إغلاق إداري دون مخالفة",
            "noreturnwarning" => "تنبيه تأخر العودة",
            "duplicateignored" => "تجاهل تكرار",
            "submitforsecurityapproval" => "رفع لاعتماد الأمن",
            _ => Fallback(value, "إجراء مسجل"),
        };

        public static string Action(string? value) => Normalize(value) switch
        {
            "entry" => "دخول",
            "return" => "عودة",
            "latereturn" => "عودة متأخرة",
            "lateattendance" => "حضور متأخر",
            "workendentry" => "دخول بعد نهاية الدوام",
            "exitauthorized" => "خروج مصرح",
            "exitunauthorized" => "خروج غير مصرح",
            "exitfinal" => "خروج نهائي",
            "workendexit" => "خروج نهاية الدوام",
            "latecheckout" => "انصراف متأخر",
            "returnafterunauthorizedexit" => "عودة بعد خروج غير مصرح",
            "unauthorizedexitneedsreview" => "خروج يحتاج مراجعة",
            "unauthorizedexitconfirmed" => "تأكيد خروج غير مصرح",
            "unauthorizedexitstopped" => "إيقاف بسبب خروج غير مصرح",
            "deniedattemptclosed" => "إغلاق محاولة مرفوضة",
            "operatornote" => "ملاحظة مشغل البوابة",
            "delegatedapproval" => "اعتماد بالتفويض",
            "submitforsecurityapproval" => "رفع لاعتماد الأمن",
            _ => Fallback(value, "إجراء مسجل"),
        };

        public static string Source(string? gateName, string? source)
        {
            if (!string.IsNullOrWhiteSpace(gateName))
            {
                return gateName.Trim();
            }

            return Normalize(source) switch
            {
                "system" => "النظام",
                "scan" => "شاشة البوابة",
                "camera" => "كاميرا الجوال",
                "manual" => "إدخال يدوي",
                "gate" => "البوابة",
                "permitscontroller" => "إدارة التصاريح",
                "scanconsolecontroller" => "مركز البوابة",
                "displaycontroller" => "شاشة البوابة",
                "public-visitor" => "بوابة الزائر",
                _ => Fallback(source, "النظام"),
            };
        }

        public static string ExecutionMethod(string? value) => Normalize(value) switch
        {
            "system" => "تلقائي",
            "manual" => "إدخال يدوي",
            "manual-override" => "تدخل يدوي",
            "camera" => "كاميرا الجوال",
            "scanner" => "قارئ الباركود",
            "barcode" => "قارئ الباركود",
            "api" => "ربط آلي",
            _ => Fallback(value, "غير محدد"),
        };

        private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

        private static string Fallback(string? value, string fallback)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return trimmed.Any(IsArabicLetter) ? trimmed : fallback;
        }

        private static bool IsArabicLetter(char value) =>
            value is >= '\u0600' and <= '\u06FF'
            or >= '\u0750' and <= '\u077F'
            or >= '\u08A0' and <= '\u08FF';
    }
}
