using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace VehiclePermitSystemWeb.Services.Uploads
{
    public sealed class ClamAvUploadThreatScanner : IUploadThreatScanner
    {
        private const int ChunkSize = 64 * 1024;
        private const int MaxResponseBytes = 4096;
        private static readonly byte[] InStreamCommand = Encoding.ASCII.GetBytes("zINSTREAM\0");
        private readonly ILogger<ClamAvUploadThreatScanner> _logger;
        private readonly string _host;
        private readonly int _port;
        private readonly bool _enabled;
        private readonly bool _required;
        private readonly TimeSpan _timeout;
        private readonly long _maxScanBytes;

        public ClamAvUploadThreatScanner(
            IConfiguration configuration,
            ILogger<ClamAvUploadThreatScanner> logger
        )
        {
            _logger = logger;
            var section = configuration.GetSection("UploadSecurity:Antivirus");
            _enabled = section.GetValue("Enabled", false);
            _required = section.GetValue("Required", false);
            _host = section["Host"]?.Trim() ?? "127.0.0.1";
            _port = Math.Clamp(section.GetValue("Port", 3310), 1, 65535);
            _timeout = TimeSpan.FromSeconds(Math.Clamp(section.GetValue("TimeoutSeconds", 30), 2, 120));
            _maxScanBytes = Math.Clamp(
                section.GetValue("MaxScanBytes", 100L * 1024 * 1024),
                1L * 1024 * 1024,
                512L * 1024 * 1024
            );
        }

        public async Task ScanAsync(
            Stream content,
            string fileName,
            CancellationToken cancellationToken = default
        )
        {
            if (!_enabled)
            {
                return;
            }

            if (!content.CanRead)
            {
                throw new InvalidOperationException("تعذر قراءة الملف لإجراء الفحص الأمني.");
            }

            var safeFileName = Path.GetFileName(fileName ?? string.Empty);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeoutSource.CancelAfter(_timeout);
            var scanToken = timeoutSource.Token;

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_host, _port, scanToken);
                await using var network = client.GetStream();
                await network.WriteAsync(InStreamCommand, scanToken);

                var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
                try
                {
                    var lengthBuffer = new byte[sizeof(uint)];
                    long totalBytes = 0;
                    while (true)
                    {
                        var bytesRead = await content.ReadAsync(
                            buffer.AsMemory(0, ChunkSize),
                            scanToken
                        );
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        totalBytes += bytesRead;
                        if (totalBytes > _maxScanBytes)
                        {
                            throw new InvalidOperationException(
                                "حجم الملف يتجاوز حد الفحص الأمني المسموح."
                            );
                        }

                        BinaryPrimitives.WriteUInt32BigEndian(
                            lengthBuffer,
                            checked((uint)bytesRead)
                        );
                        await network.WriteAsync(lengthBuffer, scanToken);
                        await network.WriteAsync(buffer.AsMemory(0, bytesRead), scanToken);
                    }

                    Array.Clear(lengthBuffer);
                    await network.WriteAsync(lengthBuffer, scanToken);
                    await network.FlushAsync(scanToken);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }

                var response = await ReadResponseAsync(network, scanToken);
                if (response.EndsWith(" OK", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (response.Contains(" FOUND", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Rejected unsafe upload {FileName}. ClamAV response: {ScanResponse}",
                        safeFileName,
                        response
                    );
                    throw new UnsafeUploadException(
                        "تم رفض الملف لأنه لم يجتز فحص الحماية من البرمجيات الضارة."
                    );
                }

                throw new InvalidOperationException("لم يُرجع محرك الحماية نتيجة فحص صالحة.");
            }
            catch (UnsafeUploadException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                HandleScannerFailure(
                    safeFileName,
                    new TimeoutException("ClamAV scan timed out.")
                );
            }
            catch (Exception ex) when (ex is not InvalidOperationException || !_required)
            {
                HandleScannerFailure(safeFileName, ex);
            }
        }

        private void HandleScannerFailure(string fileName, Exception exception)
        {
            if (_required)
            {
                _logger.LogError(
                    exception,
                    "Upload scan failed closed for {FileName}",
                    fileName
                );
                throw new InvalidOperationException(
                    "تعذر إجراء الفحص الأمني للملف حالياً. حاول مرة أخرى لاحقاً.",
                    exception
                );
            }

            _logger.LogWarning(
                exception,
                "Upload scan was unavailable for {FileName}; continuing because scanning is optional",
                fileName
            );
        }

        private static async Task<string> ReadResponseAsync(
            NetworkStream network,
            CancellationToken cancellationToken
        )
        {
            var bytes = new List<byte>(128);
            var buffer = new byte[256];
            while (bytes.Count < MaxResponseBytes)
            {
                var read = await network.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                for (var index = 0; index < read && bytes.Count < MaxResponseBytes; index++)
                {
                    if (buffer[index] == 0)
                    {
                        return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
                    }

                    bytes.Add(buffer[index]);
                }
            }

            return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
        }
    }
}
