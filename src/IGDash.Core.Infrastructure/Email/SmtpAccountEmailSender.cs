using IGDash.Core.Application.Accounts;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IGDash.Core.Infrastructure.Email;

internal sealed class SmtpAccountEmailSender(IOptions<EmailOptions> options) : IAccountEmailSender
{
    public async Task SendAsync(string recipient, string subject, string text, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = text };
        using var smtp = new MailKit.Net.Smtp.SmtpClient { Timeout = 10000 };
        var security = settings.Security switch
        {
            "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" => SecureSocketOptions.None,
            _ => throw new InvalidOperationException("Use Email:Security None, StartTls, or SslOnConnect.")
        };
        try
        {
            await smtp.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);
            if (!string.IsNullOrWhiteSpace(settings.Username))
                await smtp.AuthenticateAsync(settings.Username, settings.Password ?? "", cancellationToken);
            await smtp.SendAsync(message, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or MailKit.CommandException or MailKit.ProtocolException or MailKit.Security.AuthenticationException or System.Security.Authentication.AuthenticationException or TimeoutException)
        { throw new AccountEmailException(); }
    }
}
