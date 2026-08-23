using Discord;
using Discord.WebSocket;

namespace DiscordBot.Services;

internal interface IDiscordChannelSender
{
    Task<bool> WaitForConnectionAsync(TimeSpan timeout, CancellationToken cancellationToken);

    Task<bool> SendFileAsync(
        ulong channelId,
        Stream stream,
        string fileName,
        string? text,
        CancellationToken cancellationToken);
}

internal sealed class DiscordSocketChannelSender(DiscordSocketClient socket) : IDiscordChannelSender
{
    public async Task<bool> WaitForConnectionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            while (socket.ConnectionState != ConnectionState.Connected)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<bool> SendFileAsync(
        ulong channelId, Stream stream, string fileName, string? text, CancellationToken cancellationToken)
    {
        if (socket.GetChannel(channelId) is not IMessageChannel channel)
        {
            return false;
        }

        await channel.SendFileAsync(stream, fileName, text: text);
        return true;
    }
}
