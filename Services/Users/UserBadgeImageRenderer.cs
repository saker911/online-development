using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZXing;
using ZXing.Common;

namespace VehiclePermitSystemWeb.Services.Users
{
    internal static class UserBadgeImageRenderer
    {
        [SupportedOSPlatform("windows")]
        public static byte[] RenderCode128(
            string content,
            int width = 640,
            int height = 180,
            int margin = 8
        )
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = margin,
                    PureBarcode = true,
                },
            };

            var pixelData = writer.Write(content);
            using var bitmap = new Bitmap(
                pixelData.Width,
                pixelData.Height,
                PixelFormat.Format32bppRgb
            );
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppRgb
            );

            try
            {
                Marshal.Copy(pixelData.Pixels, 0, bitmapData.Scan0, pixelData.Pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }
}
