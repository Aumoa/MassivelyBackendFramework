namespace Master.Services;

internal class InMemorySessionService : ISessionService
{
    private class GatewayClients(string connectionId)
    {
        public readonly string ConnectionId = connectionId;

        public readonly HashSet<string> Clients = [];
    }

    private readonly Dictionary<string, GatewayClients> m_GatewayClients = [];

    public ValueTask<bool> AddClientAsync(string connectionId, string clientId)
    {
        lock (m_GatewayClients)
        {
            if (m_GatewayClients.TryGetValue(connectionId, out var gatewayClients) == false)
            {
                gatewayClients = new GatewayClients(connectionId);
                m_GatewayClients.Add(connectionId, gatewayClients);
            }

            return ValueTask.FromResult(gatewayClients.Clients.Add(clientId));
        }
    }

    public ValueTask<bool> RemoveClientAsync(string connectionId, string clientId)
    {
        lock (m_GatewayClients)
        {
            if (m_GatewayClients.TryGetValue(connectionId, out var gatewayClients))
            {
                bool removed = gatewayClients.Clients.Remove(clientId);
                if (removed && gatewayClients.Clients.Count == 0)
                {
                    m_GatewayClients.Remove(connectionId);
                }

                return ValueTask.FromResult(removed);
            }

            return ValueTask.FromResult(false);
        }
    }
}
