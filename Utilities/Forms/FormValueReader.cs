using Microsoft.AspNetCore.Http;

namespace VehiclePermitSystemWeb.Utilities.Forms
{
    public static class FormValueReader
    {
        public static string ReadString(IFormCollection form, string key, string fallback)
        {
            var value = form[key].ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        public static int ReadInt(IFormCollection form, string key, int fallback)
        {
            return int.TryParse(form[key], out var parsedValue) ? parsedValue : fallback;
        }

        public static bool ReadBool(IFormCollection form, string key, bool fallback)
        {
            var values = form[key];
            var sawFalse = false;

            for (var index = 0; index < values.Count; index++)
            {
                if (bool.TryParse(values[index], out var parsedValue))
                {
                    if (parsedValue)
                    {
                        return true;
                    }

                    sawFalse = true;
                }
            }

            return sawFalse ? false : fallback;
        }
    }
}
