using GatewayServer.Behaviors;
using PacketCore;
using Xunit;

namespace GatewayServer.Tests.Behaviors;

public sealed class ClientTests
{
    [Fact]
    public void CreateRequestsChannel_BoundsQueuedPacketsWhenConfigured()
    {
        var channel = Client.CreateRequestsChannel(maxQueuedPackets: 1);
        var first = PacketFrame.Create(PacketKind.Notify, packetId: 1, version: 1, ReadOnlyMemory<byte>.Empty);
        var second = PacketFrame.Create(PacketKind.Notify, packetId: 2, version: 1, ReadOnlyMemory<byte>.Empty);

        try
        {
            Assert.True(channel.Writer.TryWrite(first));
            Assert.False(channel.Writer.TryWrite(second));
            Assert.True(channel.Reader.TryRead(out var read));
            Assert.Same(first, read);
            read.Dispose();
            first = null!;
        }
        finally
        {
            first?.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public void CreateRequestsChannel_AllowsUnboundedOptOut()
    {
        var channel = Client.CreateRequestsChannel(maxQueuedPackets: 0);
        var first = PacketFrame.Create(PacketKind.Notify, packetId: 1, version: 1, ReadOnlyMemory<byte>.Empty);
        var second = PacketFrame.Create(PacketKind.Notify, packetId: 2, version: 1, ReadOnlyMemory<byte>.Empty);

        try
        {
            Assert.True(channel.Writer.TryWrite(first));
            Assert.True(channel.Writer.TryWrite(second));
            Assert.True(channel.Reader.TryRead(out var firstRead));
            Assert.True(channel.Reader.TryRead(out var secondRead));
            Assert.Same(first, firstRead);
            Assert.Same(second, secondRead);
            firstRead.Dispose();
            secondRead.Dispose();
            first = null!;
            second = null!;
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
        }
    }
}
