namespace OAuth2.Components.Pages.Auth;

public partial class Login
{
    private enum RenderStates
    {
        Id,
        Login,
        Register
    }

    private struct RequestScope : IDisposable
    {
        private readonly Login m_Component;

        public RequestScope(Login component)
        {
            m_Component = component;
            Interlocked.Increment(ref component.m_Requesting);
            component.StateHasChanged();
        }

        public void Dispose()
        {
            Interlocked.Decrement(ref m_Component.m_Requesting);
            m_Component.StateHasChanged();
        }
    }
}