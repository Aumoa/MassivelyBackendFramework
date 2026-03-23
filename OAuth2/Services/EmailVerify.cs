using System.Net;
using System.Net.Mail;
using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using OAuth2.Localizations;
using OAuth2.Options;

namespace OAuth2.Services;

public class EmailVerify : IDisposable
{
    private readonly AmazonSimpleEmailServiceClient m_Client;
    private readonly NavigationManager m_Navigation;
    private readonly string m_SenderAddress;

    public EmailVerify(IOptions<EmailVerifyOptions> options, NavigationManager navigation)
    {
        m_Client = new AmazonSimpleEmailServiceClient(
            options.Value.AccessKey,
            options.Value.SecretKey,
            RegionEndpoint.GetBySystemName(options.Value.Region));
        m_Navigation = navigation;
        m_SenderAddress = options.Value.SenderAddress;
    }

    public void Dispose()
    {
        m_Client.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask SendAsync(string sub, string verifyCode, MailAddress sendTo, CancellationToken cancellationToken = default)
    {
        var subStr = Uri.EscapeDataString(sub);
        var verifyCodeStr = Uri.EscapeDataString(verifyCode);

        var sendRequest = new SendEmailRequest
        {
            Source = m_SenderAddress,
            Destination = new Destination
            {
                ToAddresses = [sendTo.Address]
            },
            Message = new Message
            {
                Subject = new Content(Strings.EMAIL_VERIFY_MAIL_SUBJECT),
                Body = new Body
                {
                    Text = new Content($"{Strings.EMAIL_VERIFY_MAIL_BODY}\n\n{m_Navigation.BaseUri}email-verify/redirect?sub={subStr}&code={verifyCodeStr}")
                }
            }
        };

        await m_Client.SendEmailAsync(sendRequest, cancellationToken);
    }
}
