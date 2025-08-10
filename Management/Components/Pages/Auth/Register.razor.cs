using Microsoft.AspNetCore.Components;
using Scripting.DTO;

namespace Management.Components.Pages.Auth;

public partial class Register
{
    [Parameter] public string ID { get; set; } = string.Empty;

    private string Password { get; set; } = string.Empty;
    private string Email { get; set; } = string.Empty;
    private string? ErrorMessage { get; set; } = null;

    private bool CanSubmit => string.IsNullOrEmpty(Password) == false && string.IsNullOrEmpty(Email) == false;
    private bool CannotSubmit => !CanSubmit;

    private Task OnBackButtonClicked()
    {
        Navigation.NavigateTo("/auth/login", true);
        return Task.CompletedTask;
    }

    private async Task OnSubmitRegisterAsync()
    {
        var response = await Http.PostAsync($"{Urls.Value.Auth}/api/auth/{Uri.EscapeDataString(ID)}?password={Uri.EscapeDataString(Password)}&email={Uri.EscapeDataString(Email)}", null);
        if (response.IsSuccessStatusCode)
        {
            Navigation.NavigateTo("/auth/login", true);
        }
        else
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            var reader = new BinaryReader(stream);
            var code = (ResponseCode)reader.ReadInt32();
            switch (code)
            {
                case ResponseCode.AccountAlreadyRegistered:
                    ErrorMessage = "This ID is already registered.";
                    break;
                case ResponseCode.AccountEmailDuplicated:
                    ErrorMessage = "This email is already registered.";
                    break;
            }
        }
    }

    private void UpdateLayout(ChangeEventArgs e)
    {
        ErrorMessage = null;
        StateHasChanged();
    }
}
