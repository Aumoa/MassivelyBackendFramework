using DiscordBot.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace DiscordBot.Services;

internal sealed record ProcessedChatImage(
    ChatLogImageInput StoredImage,
    AI.ChatImage ChatImage);

internal interface IChatLogImageProcessor
{
    ValueTask<ProcessedChatImage> ProcessAsync(
        string? fileName,
        byte[] data,
        CancellationToken cancellationToken = default);
}

internal sealed class ChatLogImageProcessor : IChatLogImageProcessor
{
    private const int MaxDimension = 1024;
    private const string StoredContentType = "image/png";

    public async ValueTask<ProcessedChatImage> ProcessAsync(
        string? fileName,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        using var input = new MemoryStream(data, writable: false);
        using var image = await Image.LoadAsync(input, cancellationToken);

        image.Mutate(context =>
        {
            context.AutoOrient();

            if (image.Width <= MaxDimension && image.Height <= MaxDimension)
            {
                return;
            }

            context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxDimension, MaxDimension)
            });
        });

        await using var output = new MemoryStream();
        await image.SaveAsPngAsync(output, new PngEncoder(), cancellationToken);
        var bytes = output.ToArray();

        var storedImage = new ChatLogImageInput(
            fileName,
            StoredContentType,
            image.Width,
            image.Height,
            bytes);

        var chatImage = new AI.ChatImage
        {
            Base64 = Convert.ToBase64String(bytes),
            MediaType = StoredContentType
        };

        return new ProcessedChatImage(storedImage, chatImage);
    }
}
