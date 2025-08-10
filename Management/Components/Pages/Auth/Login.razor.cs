using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;

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

    private async Task OnInputKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(m_ID))
        {
            await OnContinueButtonClickedAsync();
        }
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
                response = await Http.GetAsync($"{Urls.Value.Auth}/api/auth/self-provide/login?code={Uri.EscapeDataString(code)}");
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    m_ErrorMessage = "ID 또는 비밀번호가 올바르지 않거나 계정이 등록되어 있지 않습니다.";
                }
                else if (response.StatusCode == HttpStatusCode.OK)
                {
                    m_ErrorMessage = null;
                    var jwtToken = await response.Content.ReadAsStringAsync();
                    Auth.InjectPrincipal(jwtToken.Trim('"'));
                    Navigation.NavigateTo("/");
                }
                else
                {
                    Logger.LogError("Response: {Response}", response.ToString());
                }
                break;
        }
    }

    private void OnRegisterButtonClicked()
    {
        Navigation.NavigateTo($"/auth/register/{Uri.EscapeDataString(m_ID)}", true);
    }
}
