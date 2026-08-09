using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using VehiclePermitSystemWeb.Services.Administration;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public sealed record SystemEmailSendResult(bool Succeeded, string Message);

    public interface ISystemEmailSender
    {
        bool IsConfigured { get; }
        Task<SystemEmailSendResult> SendAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string htmlBody,
            string textBody,
            CancellationToken cancellationToken = default
        );
    }

    public sealed class SmtpSystemEmailSender : ISystemEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmtpSystemEmailSender> _logger;

        public SmtpSystemEmailSender(
            IConfiguration configuration,
            ILogger<SmtpSystemEmailSender> logger
        )
        {
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Host"])
            && !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Username"])
            && !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Password"])
            && !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:FromAddress"]);

        public async Task<SystemEmailSendResult> SendAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string htmlBody,
            string textBody,
            CancellationToken cancellationToken = default
        )
        {
            if (!IsConfigured)
            {
                return new SystemEmailSendResult(false, "بريد الموقع غير مهيأ للإرسال.");
            }

            var host = _configuration["Email:Smtp:Host"]!.Trim();
            var port = _configuration.GetValue("Email:Smtp:Port", 587);
            var username = _configuration["Email:Smtp:Username"]!.Trim();
            var password = _configuration["Email:Smtp:Password"]!;
            var fromAddress = _configuration["Email:Smtp:FromAddress"]!.Trim();
            var fromName = _configuration["Email:Smtp:FromName"]?.Trim();
            var enableSsl = _configuration.GetValue("Email:Smtp:EnableSsl", true);

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(
                        fromAddress,
                        string.IsNullOrWhiteSpace(fromName)
                            ? ProductIdentity.DisplayName
                            : fromName
                    ),
                    Subject = subject,
                    SubjectEncoding = Encoding.UTF8,
                    BodyEncoding = Encoding.UTF8,
                    Body = textBody,
                    IsBodyHtml = false,
                };
                message.To.Add(
                    new MailAddress(
                        recipientEmail,
                        string.IsNullOrWhiteSpace(recipientName)
                            ? recipientEmail
                            : recipientName
                    )
                );
                message.AlternateViews.Add(
                    AlternateView.CreateAlternateViewFromString(
                        textBody,
                        Encoding.UTF8,
                        MediaTypeNames.Text.Plain
                    )
                );
                message.AlternateViews.Add(
                    AlternateView.CreateAlternateViewFromString(
                        htmlBody,
                        Encoding.UTF8,
                        MediaTypeNames.Text.Html
                    )
                );

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Timeout = 15000,
                };
                await client.SendMailAsync(message, cancellationToken);
                return new SystemEmailSendResult(true, "تم إرسال الرسالة.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Operational email delivery failed.");
                return new SystemEmailSendResult(
                    false,
                    "تعذر إرسال الرسالة عبر مزود البريد."
                );
            }
        }
    }
}
