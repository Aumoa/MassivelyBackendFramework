using BlazorSharedComponent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Localizations;
using OAuth2.Services;
using HostOptions = OAuth2.Options.HostOptions;

namespace OAuth2.Components.Pages.Auth;

public partial class Authorize(
    IAccounts accounts,
    IAuthorizationCodes authorizationCodes,
    IOptions<HostOptions> hostOptions,
    IClients clients,
    NavigationManager nav)
{
    private enum RenderStates
    {
        Id,
        Login
    }

    private readonly struct RequestScope : IDisposable
    {
        private readonly Authorize m_Component;

        public RequestScope(Authorize component)
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
    [SupplyParameterFromQuery(Name = "response_type")]
    public string ResponseType { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "redirect_uri")]
    public string RedirectUri { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "client_id")]
    public string ClientId { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "scope")]
    public string Scope { get; set; } = "profile";

    [Parameter]
    [SupplyParameterFromQuery(Name = "state")]
    public string? State { get; set; }

    private RenderStates m_RenderState = RenderStates.Id;
    private string m_ID = string.Empty;
    private string m_Password = string.Empty;
    private int m_Requesting = 0;
    private string m_ErrorMessageId = string.Empty;
    private string m_ErrorMessagePassword = string.Empty;
    private FloatingInput m_InputPassword = null!;

    private bool IdError => string.IsNullOrEmpty(m_ErrorMessageId) == false;
    private bool PasswordError => string.IsNullOrEmpty(m_ErrorMessagePassword) == false;

    private bool CanSubmit => string.IsNullOrEmpty(m_ID) == false;
    private bool Requesting => m_Requesting > 0;

    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrWhiteSpace(ResponseType) || string.IsNullOrWhiteSpace(RedirectUri) || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(Scope))
        {
            nav.NavigateTo("/error");
            return;
        }

        if (ResponseType != "code")
        {
            nav.NavigateTo("/error");
            return;
        }

        if (ClientId == hostOptions.Value.ClientId && RedirectUri == nav.BaseUri + "redirect")
        {
            return;
        }

        var targetClient = await clients.GetClientAsync(ClientId);
        if (targetClient == null)
        {
            nav.NavigateTo("/error");
            return;
        }
    }

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
    }

    private void OnRegister(MouseEventArgs _)
    {
        var uri = new Uri(nav.Uri);
        nav.NavigateTo($"/register?return_url={Uri.EscapeDataString(uri.PathAndQuery)}");
    }

    private void OnReturn(MouseEventArgs _)
    {
        m_RenderState = RenderStates.Id;
    }

    private async Task OnContinueAsync()
    {
        switch (m_RenderState)
        {
            case RenderStates.Id:
                await OnContinue_StateIdAsync();
                break;
            case RenderStates.Login:
                await OnContinue_StateLoginAsync();
                break;
        }
    }

    private async ValueTask OnContinue_StateLoginAsync()
    {
        using var scope1 = new RequestScope(this);

        if (string.IsNullOrEmpty(m_ID))
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_REQUIRED;
            return;
        }

        if (string.IsNullOrEmpty(m_Password))
        {
            m_ErrorMessagePassword = Strings.LOGIN_VALIDATION_ERROR_PW_REQUIRED;
            return;
        }

        if (await accounts.VerifyAsync(m_ID, m_Password) == false)
        {
            m_ErrorMessagePassword = Strings.LOGIN_VALIDATION_ERROR_PW_INVALID;
            return;
        }

        var code = await authorizationCodes.PushAsync(new AuthorizationCodeBody(m_ID, ClientId, Scope, RedirectUri));
        var query = new Dictionary<string, string?>
        {
            ["code"] = code
        };
        if (string.IsNullOrEmpty(State) == false)
        {
            query.Add("state", State);
        }

        var uri = QueryHelpers.AddQueryString(RedirectUri, query);
        nav.NavigateTo(uri, forceLoad: true);
    }

    private async Task OnContinue_StateIdAsync()
    {
        using var scope1 = new RequestScope(this);

        if (string.IsNullOrEmpty(m_ID))
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_REQUIRED;
            return;
        }

        if (await accounts.ExistsAsync(m_ID) == false)
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_NOT_FOUND;
            return;
        }

        m_RenderState = RenderStates.Login;
        StateHasChanged();

        _ = InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(0.4));
            await m_InputPassword.FocusAsync();
        });
    }

    private Task OnChangedAsync()
    {
        m_ErrorMessageId = string.Empty;
        m_ErrorMessagePassword = string.Empty;
        return Task.CompletedTask;
    }
}