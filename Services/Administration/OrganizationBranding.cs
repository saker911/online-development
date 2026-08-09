using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using System.Security.Cryptography;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Uploads;
using VehiclePermitSystemWeb.Utilities.Deployment;

namespace VehiclePermitSystemWeb.Services.Administration
{
    public static class OrganizationBranding
    {
        public const string DefaultOrganizationName = "الجهة المستخدمة";
        public const string DefaultLogoPath = "images/tasreehgate-logo.png";

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
        private const int MaxImageDimension = 4096;
        private const long MaxImagePixels = 12_000_000;

        public sealed record ProcessedImage(byte[] Data, string ContentType, string Extension);

        public static async Task<string> SaveAsync(
            IFormFile file,
            string prefix,
            DateTime utcNow,
            IUploadThreatScanner? threatScanner = null,
            CancellationToken cancellationToken = default
        )
        {
            _ = utcNow;
            var processed = await ProcessAsync(file, threatScanner, cancellationToken);
            var uploadsFolder = AppStoragePaths.GetAdministrationUploadsRoot();
            var safePrefix = string.Equals(prefix, "signature", StringComparison.OrdinalIgnoreCase)
                ? "signature"
                : "logo";
            var randomName = Convert.ToHexString(RandomNumberGenerator.GetBytes(12))
                .ToLowerInvariant();
            var fileName = $"{safePrefix}-{randomName}{processed.Extension}";
            var fullPath = Path.Combine(uploadsFolder, fileName);
            var stagingPath = AppStoragePaths.GetPersistentDataPath(
                Path.Combine("upload-staging", $"{randomName}.tmp")
            );

            try
            {
                await File.WriteAllBytesAsync(stagingPath, processed.Data, cancellationToken);
                File.Move(stagingPath, fullPath, overwrite: false);
            }
            finally
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }
            }

            return Path.Combine("uploads", "administration", fileName).Replace('\\', '/');
        }

        public static async Task<ProcessedImage> ProcessAsync(
            IFormFile file,
            IUploadThreatScanner? threatScanner = null,
            CancellationToken cancellationToken = default
        )
        {
            if (file.Length <= 0 || file.Length > MaxImageBytes)
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

            var expectedContentType = safeExtension switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg",
            };
            if (!string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "نوع ملف الصورة لا يطابق الامتداد المحدد."
                );
            }

            try
            {
                if (threatScanner != null)
                {
                    await using var scanStream = file.OpenReadStream();
                    await threatScanner.ScanAsync(
                        scanStream,
                        file.FileName,
                        cancellationToken
                    );
                }

                await using (var formatStream = file.OpenReadStream())
                {
                    var detectedFormat = await Image.DetectFormatAsync(
                        formatStream,
                        cancellationToken
                    );
                    if (
                        detectedFormat == null
                        || !string.Equals(
                            detectedFormat.DefaultMimeType,
                            expectedContentType,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        throw new InvalidOperationException(
                            "محتوى الصورة لا يطابق نوع الملف المعلن."
                        );
                    }
                }

                await using (var identifyStream = file.OpenReadStream())
                {
                    var imageInfo = await Image.IdentifyAsync(identifyStream, cancellationToken);
                    if (imageInfo == null)
                    {
                        throw new InvalidOperationException("محتوى ملف الصورة غير صالح.");
                    }

                    ValidateDimensions(imageInfo.Width, imageInfo.Height);
                }

                await using var input = file.OpenReadStream();
                using var image = await Image.LoadAsync(input, cancellationToken);
                ValidateDimensions(image.Width, image.Height);

                if (image.Frames.Count != 1)
                {
                    throw new InvalidOperationException("الصور المتحركة غير مسموح بها.");
                }

                image.Metadata.ExifProfile = null;
                image.Metadata.IccProfile = null;
                image.Metadata.XmpProfile = null;

                IImageEncoder encoder = safeExtension switch
                {
                    ".png" => new PngEncoder(),
                    ".webp" => new WebpEncoder { Quality = 88 },
                    _ => new JpegEncoder { Quality = 90 },
                };
                var contentType = safeExtension switch
                {
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "image/jpeg",
                };

                await using var output = new MemoryStream();
                await image.SaveAsync(output, encoder, cancellationToken);
                if (output.Length > MaxImageBytes)
                {
                    throw new InvalidOperationException(
                        "حجم الصورة بعد المعالجة يتجاوز 2 ميجابايت. استخدم صورة أصغر."
                    );
                }

                return new ProcessedImage(output.ToArray(), contentType, safeExtension);
            }
            catch (UnknownImageFormatException)
            {
                throw new InvalidOperationException("محتوى ملف الصورة غير صالح.");
            }
            catch (InvalidImageContentException)
            {
                throw new InvalidOperationException("تعذر قراءة الصورة بأمان.");
            }
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (
                width <= 0
                || height <= 0
                || width > MaxImageDimension
                || height > MaxImageDimension
                || (long)width * height > MaxImagePixels
            )
            {
                throw new InvalidOperationException(
                    "أبعاد الصورة كبيرة جدًا. الحد الأقصى 4096 بكسل و12 مليون بكسل إجمالًا."
                );
            }
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

    }
}
