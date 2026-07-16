using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ZXing;
using ZXing.Common;

namespace VehiclePermitSystemWeb.Utilities.Barcodes
{
    internal static class BarcodePngRenderer
    {
        private static readonly byte[] PngSignature =
        {
            137, 80, 78, 71, 13, 10, 26, 10,
        };

        public static byte[] Render(
            string content,
            BarcodeFormat format,
            int width,
            int height,
            int margin = 0
        )
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = format,
                Options = new EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = margin,
                    PureBarcode = true,
                },
            };

            var pixelData = writer.Write(content);
            return EncodeGrayscale(pixelData.Pixels, pixelData.Width, pixelData.Height);
        }

        private static byte[] EncodeGrayscale(byte[] pixels, int width, int height)
        {
            var scanlines = new byte[(width + 1) * height];
            for (var y = 0; y < height; y++)
            {
                var scanlineOffset = y * (width + 1);
                scanlines[scanlineOffset] = 0;

                for (var x = 0; x < width; x++)
                {
                    var pixelOffset = ((y * width) + x) * 4;
                    scanlines[scanlineOffset + x + 1] = pixels[pixelOffset];
                }
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            {
                zlib.Write(scanlines);
            }

            using var png = new MemoryStream();
            png.Write(PngSignature);

            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
            header[8] = 8;
            header[9] = 0;
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "IDAT", compressed.ToArray());
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void WriteChunk(Stream destination, string type, byte[] data)
        {
            var typeBytes = Encoding.ASCII.GetBytes(type);
            Span<byte> number = stackalloc byte[4];

            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            destination.Write(number);
            destination.Write(typeBytes);
            destination.Write(data);

            BinaryPrimitives.WriteUInt32BigEndian(number, ComputeCrc(typeBytes, data));
            destination.Write(number);
        }

        private static uint ComputeCrc(byte[] type, byte[] data)
        {
            var crc = uint.MaxValue;
            crc = UpdateCrc(crc, type);
            crc = UpdateCrc(crc, data);
            return ~crc;
        }

        private static uint UpdateCrc(uint crc, byte[] bytes)
        {
            foreach (var value in bytes)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? 0xedb88320U ^ (crc >> 1) : crc >> 1;
                }
            }

            return crc;
        }
    }
}
