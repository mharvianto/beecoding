using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace BeeCoding.Services;

/// <summary>Outgoing mail settings — config section <c>Email</c> (env <c>Email__Host</c> etc.).</summary>
public sealed class EmailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;   // STARTTLS/TLS; turn off only for a local relay
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "BeeCoding";
}

/// <summary>
/// Minimal SMTP sender, used for password-reset links. When no host/from address is configured
/// the feature is simply off (<see cref="IsConfigured"/> is false) and the UI hides it — an
/// admin can still issue a reset link by hand from the Admin panel.
/// </summary>
public sealed class EmailService(IOptions<EmailOptions> opt, ILogger<EmailService> log)
{
    private readonly EmailOptions _o = opt.Value;
    private readonly ILogger<EmailService> _log = log;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.Host) && !string.IsNullOrWhiteSpace(_o.From);

    /// <summary>Send one plain-text message. Never throws: a failure is logged and returns false.</summary>
    public async Task<bool> SendAsync(string to, string subject, string body)
    {
        if (!IsConfigured) return false;
        try
        {
            using var client = new SmtpClient(_o.Host, _o.Port)
            {
                EnableSsl = _o.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15_000,
                Credentials = string.IsNullOrEmpty(_o.User) ? null : new NetworkCredential(_o.User, _o.Password),
            };
            using var msg = new MailMessage
            {
                From = new MailAddress(_o.From, _o.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false,
            };
            msg.To.Add(to);
            await client.SendMailAsync(msg);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send email to {To}", to);
            return false;
        }
    }
}
