using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using OAuth2.Components.Shared;
using OAuth2.DTO;
using OAuth2.Localizations;
using OAuth2.Services;

namespace OAuth2.Components.Pages;

public partial class Register(
    IAccounts accounts,
    IAccountClaims claims,
    NavigationManager nav)
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
    private string m_Email = string.Empty;
    private string m_Password = string.Empty;
    private int m_Requesting = 0;
    private string m_ErrorMessageId = string.Empty;
    private string m_ErrorMessagePassword = string.Empty;
    private string m_ErrorMessageEmail = string.Empty;

    private bool IdError => string.IsNullOrEmpty(m_ErrorMessageId) == false;
    private bool PasswordError => string.IsNullOrEmpty(m_ErrorMessagePassword) == false;
    private bool EmailError => string.IsNullOrEmpty(m_ErrorMessageEmail) == false;

    private bool CanSubmit => string.IsNullOrEmpty(m_ID) == false && string.IsNullOrEmpty(m_Password) == false && string.IsNullOrEmpty(m_Email) == false && ValidEmailRegex().IsMatch(m_Email);
    private bool Requesting => m_Requesting > 0;

    private IEnumerable<string> GetErrorMessages()
    {
        if (string.IsNullOrEmpty(m_ErrorMessageId) == false)
        {
            yield return m_ErrorMessageId;
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

        if (string.IsNullOrEmpty(m_Password))
        {
            m_ErrorMessagePassword = Strings.LOGIN_VALIDATION_ERROR_PW_REQUIRED;
            return;
        }

        if (string.IsNullOrEmpty(m_Email))
        {
            m_ErrorMessageEmail = Strings.LOGIN_VALIDATION_ERROR_EMAIL_REQUIRED;
            return;
        }

        if (ValidEmailRegex().IsMatch(m_Email) == false)
        {
            m_ErrorMessageEmail = Strings.LOGIN_VALIDATION_ERROR_EMAIL_INVALID_FORMAT;
            return;
        }

        await accounts.AddAsync(m_ID, m_Password);
        await claims.AddClaimAsync(m_ID, ClaimName.Email, m_Email);

        if (string.IsNullOrEmpty(ReturnUrl) == false)
        {
            nav.NavigateTo(ReturnUrl);
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