namespace VehiclePermitSystemWeb.Services.Uploads
{
    public interface IUploadThreatScanner
    {
        Task ScanAsync(
            Stream content,
            string fileName,
            CancellationToken cancellationToken = default
        );
    }

    public sealed class UnsafeUploadException : InvalidOperationException
    {
        public UnsafeUploadException(string message)
            : base(message) { }
    }
}
