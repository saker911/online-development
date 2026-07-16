using ZXing;
using VehiclePermitSystemWeb.Utilities.Barcodes;

namespace VehiclePermitSystemWeb.Services.Users
{
    internal static class UserBadgeImageRenderer
    {
        public static byte[] RenderCode128(
            string content,
            int width = 640,
            int height = 180,
            int margin = 8
        )
        {
            return BarcodePngRenderer.Render(content, BarcodeFormat.CODE_128, width, height, margin);
        }
    }
}
