using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Management.Components.Pages.Auth;

public partial class Login
{
    private string m_ID = string.Empty;
    private string m_Password = string.Empty;
    private string? m_ErrorMessage = null;
    private int m_Step = 0;

    private void ClearErrorMessage(ChangeEventArgs e)
    {
        m_ID = e.Value?.ToString() ?? string.Empty;
        m_ErrorMessage = string.Empty;
    }

    private async Task OnContinueButtonClickedAsync()
    {
        switch (m_Step)
        {
            case 0:
                var response = await Http.GetAsync($"{Urls.Value.Auth}/api/auth?id={Uri.EscapeDataString(m_ID)}");
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    m_ErrorMessage = "ID does not exist.";
                }
                else
                {
                    m_ErrorMessage = string.Empty;
                    m_Step = 1;
                }
                break;
            case 1:
                var code = $"{m_ID}${m_Password}";
                var loginUrl = $"{Urls.Value.Auth}/api/auth/self-provide/login?code={Uri.EscapeDataString(code)}";
                var result = await JSRuntime.InvokeAsync<dynamic>("authInterop.login", loginUrl);
                var ok = (bool)result.GetProperty("ok").GetBoolean();
                var status = (int)result.GetProperty("status").GetInt32();
                if (!ok || status == 401)
                {
                    m_ErrorMessage = "The ID or password is incorrect, or the account is not registered.";
                }
                else if (ok)
                {
                    m_ErrorMessage = null;
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

    private void OnRegisterButtonClicked()
    {
        Navigation.NavigateTo($"/auth/register/{Uri.EscapeDataString(m_ID)}", true);
    }
}
