using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using OAuth2.Localizations;
using OAuth2.Options;

namespace OAuth2.Services;

public class EmailVerify
{
    private readonly SmtpClient m_Client;
    private readonly NavigationManager m_Navigation;
    private readonly string m_Sender;

    public EmailVerify(IOptions<EmailVerifyOptions> options, NavigationManager navigation)
    {
        m_Client = new SmtpClient(options.Value.Host, options.Value.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(options.Value.UserId, options.Value.Secret)
        };

        m_Navigation = navigation;
        m_Sender = options.Value.Sender;
    }

    public async ValueTask SendAsync(string sub, string verifyCode, MailAddress sendTo, CancellationToken cancellationToken = default)
    {
        var subStr = Uri.EscapeDataString(sub);
        var verifyCodeStr = Uri.EscapeDataString(verifyCode);

        var message = new MailMessage
        {
            From = new MailAddress(m_Sender, "OAuth2"),
            Subject = Strings.EMAIL_VERIFY_MAIL_SUBJECT,
            Body = $"{Strings.EMAIL_VERIFY_MAIL_BODY}\n\n{m_Navigation.BaseUri}email-verify/redirect?sub={subStr}&code={verifyCodeStr}"
        };

        message.To.Add(sendTo);

        await m_Client.SendMailAsync(message, cancellationToken);
    }
}
