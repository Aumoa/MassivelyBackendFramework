using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OAuth2.Localizations;
using OAuth2.Services;

namespace OAuth2.Components.Pages.Auth;

public partial class Register(
    IAccounts accounts,
    IAccountClaims accountClaims,
    NavigationManager nav,
    IJSRuntime js,
    EmailVerify emailVerify)
{
    private readonly struct RequestScope : IDisposable
    {
        private readonly Register m_Component;

        public RequestScope(Register component)
        {
            m_Component = component;
            Interlocked.Increment(ref component.m_Requesting);
            component.StateHasChanged();
        }

        void IDisposable.Dispose()
        {
            Interlocked.Decrement(ref m_Component.m_Requesting);
            m_Component.StateHasChanged();
        }
    }

    [Parameter]
    [SupplyParameterFromQuery(Name = "return_url")]
    public string? ReturnUrl { get; set; }

    private string m_ID = string.Empty;
    private string m_Name = string.Empty;
    private string m_Email = string.Empty;
    private string m_Password = string.Empty;
    private int m_Requesting = 0;
    private string m_ErrorMessageId = string.Empty;
    private string m_ErrorMessageName = string.Empty;
    private string m_ErrorMessagePassword = string.Empty;
    private string m_ErrorMessageEmail = string.Empty;

    private bool IdError => string.IsNullOrEmpty(m_ErrorMessageId) == false;
    private bool NameError => string.IsNullOrEmpty(m_ErrorMessageName) == false;
    private bool PasswordError => string.IsNullOrEmpty(m_ErrorMessagePassword) == false;
    private bool EmailError => string.IsNullOrEmpty(m_ErrorMessageEmail) == false;

    private bool CanSubmit =>
        string.IsNullOrEmpty(m_ID) == false &&
        string.IsNullOrEmpty(m_Name) == false &&
        string.IsNullOrEmpty(m_Password) == false &&
        string.IsNullOrEmpty(m_Email) == false &&
        ValidEmailRegex().IsMatch(m_Email);
    private bool Requesting => m_Requesting > 0;

    private IEnumerable<string> GetErrorMessages()
    {
        if (string.IsNullOrEmpty(m_ErrorMessageId) == false)
        {
            yield return m_ErrorMessageId;
        }

        if (string.IsNullOrEmpty(m_ErrorMessageName) == false)
        {
            yield return m_ErrorMessageName;
        }

        if (string.IsNullOrEmpty(m_ErrorMessagePassword) == false)
        {
            yield return m_ErrorMessagePassword;
        }

        if (string.IsNullOrEmpty(m_ErrorMessageEmail) == false)
        {
            yield return m_ErrorMessageEmail;
        }
    }

    private async Task OnContinueAsync()
    {
        if (string.IsNullOrEmpty(m_ID))
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_REQUIRED;
            return;
        }

        bool exists = await accounts.ExistsAsync(m_ID);
        if (exists)
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_ALREADY_EXISTS;
            return;
        }

        if (string.IsNullOrWhiteSpace(m_Name))
        {
            m_ErrorMessageName = Strings.LOGIN_VALIDATION_ERROR_NAME_REQUIRED;
            return;
        }

        if (string.IsNullOrWhiteSpace(m_Password))
        {
            m_ErrorMessagePassword = Strings.LOGIN_VALIDATION_ERROR_PW_REQUIRED;
            return;
        }

        if (string.IsNullOrWhiteSpace(m_Email))
        {
            m_ErrorMessageEmail = Strings.LOGIN_VALIDATION_ERROR_EMAIL_REQUIRED;
            return;
        }

        if (ValidEmailRegex().IsMatch(m_Email) == false)
        {
            m_ErrorMessageEmail = Strings.LOGIN_VALIDATION_ERROR_EMAIL_INVALID_FORMAT;
            return;
        }

        Interlocked.Increment(ref m_Requesting);
        StateHasChanged();
        try
        {
            var locale = await js.InvokeAsync<string[]>("getUserLocale");
            var verifyCode = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            var sub = await accounts.AddAsync(m_ID, m_Password, m_Name, m_Email, verifyCode);
            await accountClaims.AddClaimAsync(m_ID, JwtRegisteredClaimNames.Locale, locale[0]);
            await accountClaims.AddClaimAsync(m_ID, JwtRegisteredClaimNames.ZoneInfo, locale[1]);
            await emailVerify.SendAsync(sub, verifyCode, new MailAddress(m_Email));

            if (string.IsNullOrEmpty(ReturnUrl) == false
                && ReturnUrl.StartsWith('/')
                && !ReturnUrl.StartsWith("//")
                && Uri.TryCreate(ReturnUrl, UriKind.Relative, out _))
            {
                nav.NavigateTo(ReturnUrl);
            }
        }
        catch (Exception)
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_EMAIL_ALREADY_EXISTS;
        }
        finally
        {
            Interlocked.Decrement(ref m_Requesting);
            StateHasChanged();
        }
    }

    private Task OnChangedAsync()
    {
        m_ErrorMessageId = string.Empty;
        m_ErrorMessagePassword = string.Empty;
        m_ErrorMessageEmail = string.Empty;
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ValidEmailRegex();
}