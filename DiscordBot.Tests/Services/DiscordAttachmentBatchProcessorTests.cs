using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DiscordAttachmentBatchProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ReturnsSuccessfulResultsAndReportsFailures()
    {
        var failures = new List<(int Attachment, string Message)>();

        var results = await DiscordAttachmentBatchProcessor.ProcessAsync(
            [1, 2, 3],
            attachment =>
            {
                if (attachment == 2)
                {
                    throw new InvalidOperationException("download failed");
                }

                return Task.FromResult(attachment * 10);
            },
            (attachment, exception) => failures.Add((attachment, exception.Message)));

        Assert.Equal([10, 30], results);
        var failure = Assert.Single(failures);
        Assert.Equal(2, failure.Attachment);
        Assert.Equal("download failed", failure.Message);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotInvokeDelegatesForEmptyInput()
    {
        var processCalled = false;
        var errorCalled = false;

        var results = await DiscordAttachmentBatchProcessor.ProcessAsync<int, int>(
            [],
            attachment =>
            {
                processCalled = true;
                return Task.FromResult(attachment);
            },
            (_, _) => errorCalled = true);

        Assert.Empty(results);
        Assert.False(processCalled);
        Assert.False(errorCalled);
    }
}
