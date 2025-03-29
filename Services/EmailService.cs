using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace DAM.Services
{
    public class EmailMessage
    {
        public string EmailAddress { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string? AttachmentPath { get; set; }
    }

    public interface IEmailService
    {
        Task SendEmail(string emailAddress, string content, string subject);
        Task SendEmail(string emailAddress, string content, string subject, string attachmentPath);
        Task SendEmailFromMessage(EmailMessage message);
    }

    public class EmailService : IEmailService
    {
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPass;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _smtpServer = configuration["Smtp:Host"] ?? "smtp.gmail.com";
            _smtpPort = int.TryParse(configuration["Smtp:Port"], out var port) ? port : 587;
            _smtpUser = configuration["Smtp:Username"] ?? "";
            _smtpPass = configuration["Smtp:Password"] ?? "";
            _logger = logger;
        }

        public async Task SendEmail(string emailAddress, string content, string subject)
        {
            try
            {
                using (var client = new SmtpClient(_smtpServer, _smtpPort))
                {
                    client.Credentials = new NetworkCredential(_smtpUser, _smtpPass);
                    client.EnableSsl = true;

                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpUser),
                        Subject = subject,
                        Body = content,
                        IsBodyHtml = true,
                    };
                    mailMessage.To.Add(emailAddress);

                    await client.SendMailAsync(mailMessage);
                    _logger.LogInformation("Email sent to {EmailAddress} with subject: {Subject}", emailAddress, subject);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {EmailAddress}", emailAddress);
                throw;
            }
        }

        public async Task SendEmail(string emailAddress, string content, string subject, string attachmentPath)
        {
            try
            {
                using (var client = new SmtpClient(_smtpServer, _smtpPort))
                {
                    client.Credentials = new NetworkCredential(_smtpUser, _smtpPass);
                    client.EnableSsl = true;

                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpUser),
                        Subject = subject,
                        Body = content,
                        IsBodyHtml = true,
                    };
                    mailMessage.To.Add(emailAddress);

                    if (!string.IsNullOrEmpty(attachmentPath))
                    {
                        var attachment = new Attachment(attachmentPath);
                        mailMessage.Attachments.Add(attachment);
                    }

                    await client.SendMailAsync(mailMessage);
                    _logger.LogInformation("Email with attachment sent to {EmailAddress}", emailAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email with attachment to {EmailAddress}", emailAddress);
                throw;
            }
        }

        public async Task SendEmailFromMessage(EmailMessage message)
        {
            if (message.AttachmentPath != null)
            {
                await SendEmail(message.EmailAddress, message.Content, message.Subject, message.AttachmentPath);
            }
            else
            {
                await SendEmail(message.EmailAddress, message.Content, message.Subject);
            }
        }
    }
}
