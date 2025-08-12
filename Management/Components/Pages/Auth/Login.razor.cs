using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Management.Components.Pages.Auth;

public partial class Login
{
    public enum RenderStates
    {
        None,
        Login
    }

    public string ID { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;

    private int m_Requesting;
    private readonly List<RenderStates> m_States = [RenderStates.None];
    private CancellationTokenSource? m_CancellationSource;

    public bool HasError => string.IsNullOrEmpty(ErrorMessage) == false;
    public bool Requesting => m_Requesting > 0;

    private readonly struct RequestingScope : IDisposable
    {
        private readonly Login m_Owner;

        public RequestingScope(Login owner)
        {
            m_Owner = owner;
            Interlocked.Increment(ref m_Owner.m_Requesting);
        }

        public void Dispose()
        {
            Interlocked.Decrement(ref m_Owner.m_Requesting);
        }
    }

    private void ClearErrorMessage(ChangeEventArgs e)
    {
        ErrorMessage = string.Empty;
    }

    private async Task OnContinueButtonClickedAsync()
    {
        using var scope1 = new RequestingScope(this);
        switch (m_States.Last())
        {
            case RenderStates.None:
                var response = await Http.GetAsync($"{Urls.Value.Auth}/api/auth?id={Uri.EscapeDataString(ID)}");
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    ErrorMessage = LoginStrings.VALIDATION_ERROR_ID;
                }
                else
                {
                    ErrorMessage = string.Empty;
                Transit(RenderStates.Login);
                }
                break;
            case RenderStates.Login:
                var code = $"{ID}${Password}";
                var loginUrl = $"{Urls.Value.Auth}/api/auth/self-provide/login?code={Uri.EscapeDataString(code)}";
                var result = await JSRuntime.InvokeAsync<dynamic>("authInterop.login", loginUrl);
                var ok = (bool)result.GetProperty("ok").GetBoolean();
                var status = (int)result.GetProperty("status").GetInt32();
                if (!ok || status == 401)
                {
                    ErrorMessage = LoginStrings.VALIDATION_ERROR_PW;
                }
                else if (ok)
                {
                    ErrorMessage = string.Empty;
                    Auth.ReloadPrincipal();
                    Navigation.NavigateTo("/", forceLoad: true);
                }
                else
                {
                    Logger.LogError("Login failed. Status: {Status}", status);
                }
                break;
        }
    }

    private async void Transit(RenderStates nextState)
    {
        if (m_CancellationSource != null)
        {
            m_CancellationSource.Cancel();
            m_CancellationSource.Dispose();
        }

        var s = m_States.Last();
        m_States.Clear();
        m_States.AddRange([s, nextState]);

        m_CancellationSource = new CancellationTokenSource();
        var cancellationToken = m_CancellationSource.Token;
        await Task.Delay(TimeSpan.FromSeconds(0.4));

        if (cancellationToken.IsCancellationRequested == false)
        {
            m_States.Clear();
            m_States.Add(nextState);
            m_CancellationSource.Dispose();
            m_CancellationSource = null;
            StateHasChanged();
        }
    }

    private void OnRegisterButtonClicked()
    {
        Navigation.NavigateTo($"/auth/register/{Uri.EscapeDataString(ID)}", true);
    }
}
