using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Management.Components.Pages.Auth;

public partial class Login
{
    private string m_ID = string.Empty;
    private string m_Password = string.Empty;
    private string m_ErrorMessage = string.Empty;
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
                break;
        }
    }

    private void OnRegisterButtonClicked()
    {
        Navigation.NavigateTo($"/auth/register/{Uri.EscapeDataString(m_ID)}", true);
    }
}
