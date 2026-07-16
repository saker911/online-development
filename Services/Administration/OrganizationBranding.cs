using Microsoft.AspNetCore.Http;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Utilities.Deployment;

namespace VehiclePermitSystemWeb.Services.Administration
{
    public static class OrganizationBranding
    {
        public const string DefaultOrganizationName = "الجهة المستخدمة";
        public const string DefaultLogoPath = "images/organization-placeholder.svg";

        public static string GetOrganizationName(AdministrationSettings? settings)
        {
            var organizationName = (settings?.OrganizationName ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(organizationName)
                ? DefaultOrganizationName
                : organizationName;
        }

        public static string GetLogoPath(AdministrationSettings? settings)
        {
            var logoPath = (settings?.LogoPath ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('~', '/');

            if (string.IsNullOrWhiteSpace(logoPath))
            {
                return DefaultLogoPath;
            }

            if (
                logoPath.StartsWith("uploads/administration/", StringComparison.OrdinalIgnoreCase)
                && !File.Exists(AppStoragePaths.ResolveUploadPhysicalPath(logoPath))
            )
            {
                return DefaultLogoPath;
            }

            return logoPath;
        }
    }

    public static class AdministrationImageStorage
    {
        private const long MaxImageBytes = 2 * 1024 * 1024;

        public static async Task<string> SaveAsync(
            IFormFile file,
            string prefix,
            DateTime utcNow
        )
        {
            if (file.Length > MaxImageBytes)
            {
                throw new InvalidOperationException("حجم الصورة يجب ألا يتجاوز 2 ميجابايت.");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var safeExtension = extension switch
            {
                ".png" or ".jpg" or ".jpeg" or ".webp" => extension,
                _ => throw new InvalidOperationException(
                    "صيغة الصورة يجب أن تكون PNG أو JPG أو JPEG أو WEBP."
                ),
            };

            var allowedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "image/png",
                "image/jpeg",
                "image/webp",
            };
            if (!allowedMimeTypes.Contains(file.ContentType ?? string.Empty))
            {
                throw new InvalidOperationException("نوع ملف الصورة غير مسموح.");
            }

            if (!await HasAllowedImageSignatureAsync(file, safeExtension))
            {
                throw new InvalidOperationException("محتوى ملف الصورة لا يطابق الصيغة المحددة.");
            }

            var uploadsFolder = AppStoragePaths.GetAdministrationUploadsRoot();
            var safePrefix = string.Equals(prefix, "signature", StringComparison.OrdinalIgnoreCase)
                ? "signature"
                : "logo";
            var fileName = $"{safePrefix}-{utcNow:yyyyMMddHHmmssfff}{safeExtension}";
            var fullPath = Path.Combine(uploadsFolder, fileName);

            await using var stream = File.Create(fullPath);
            await file.CopyToAsync(stream);

            return Path.Combine("uploads", "administration", fileName).Replace('\\', '/');
        }

        public static void DeleteIfManaged(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return;
            }

            var normalizedPath = relativePath.Replace('\\', '/').TrimStart('~', '/');
            if (
                !normalizedPath.StartsWith(
                    "uploads/administration/",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            var physicalPath = AppStoragePaths.ResolveUploadPhysicalPath(normalizedPath);
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }

        private static async Task<bool> HasAllowedImageSignatureAsync(
            IFormFile file,
            string extension
        )
        {
            var buffer = new byte[12];
            await using var stream = file.OpenReadStream();
            var bytesRead = await stream.ReadAsync(buffer);

            return extension switch
            {
                ".png" => bytesRead >= 8
                    && buffer[..8]
                        .SequenceEqual(
                            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
                        ),
                ".jpg" or ".jpeg" => bytesRead >= 3
                    && buffer[0] == 0xFF
                    && buffer[1] == 0xD8
                    && buffer[2] == 0xFF,
                ".webp" => bytesRead >= 12
                    && buffer[0] == (byte)'R'
                    && buffer[1] == (byte)'I'
                    && buffer[2] == (byte)'F'
                    && buffer[3] == (byte)'F'
                    && buffer[8] == (byte)'W'
                    && buffer[9] == (byte)'E'
                    && buffer[10] == (byte)'B'
                    && buffer[11] == (byte)'P',
                _ => false,
            };
        }
    }
}
