using Microsoft.JSInterop;

namespace Management.Components.Pages.Auth;

public partial class Logout
{
    protected override async Task OnInitializedAsync()
    {
        await JSRuntime.InvokeVoidAsync("authInterop.logout", $"{Urls.Value.Auth}/api/auth/logout");
        Navigation.NavigateTo("/auth/login", true);
    }
}