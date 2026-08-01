using System.Text;
using ZXing;
using ZXing.Common;

namespace VehiclePermitSystemWeb.Utilities.Barcodes
{
    public static class BarcodeSvgRenderer
    {
        public static string RenderQr(string value, int size = 320, int margin = 3)
        {
            var pixels = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions
                {
                    Height = size,
                    Width = size,
                    Margin = margin,
                    PureBarcode = true,
                },
            }.Write(value);

            var path = new StringBuilder();
            for (var y = 0; y < pixels.Height; y++)
            {
                for (var x = 0; x < pixels.Width; x++)
                {
                    var offset = (y * pixels.Width + x) * 4;
                    if (pixels.Pixels[offset] < 128)
                    {
                        path.Append($"M{x} {y}h1v1h-1z");
                    }
                }
            }

            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {pixels.Width} {pixels.Height}\" shape-rendering=\"crispEdges\"><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path d=\"{path}\" fill=\"#07111f\"/></svg>";
        }
    }
}
