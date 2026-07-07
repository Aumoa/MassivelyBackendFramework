using System.Data;
using System.Net;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal sealed class MySqlAccountPictures(
    IOptions<MySqlOptions> mySqlOptions,
    IOptions<AccountPictureOptions> options,
    HttpClient httpClient,
    ILogger<MySqlAccountPictures> logger)
    : MySqlDbContext(mySqlOptions.Value), IAccountPictures
{
    private const string PngContentType = "image/png";
    private const string JpegContentType = "image/jpeg";

    private readonly AccountPictureOptions m_Options = options.Value;

    public async ValueTask<AccountPicture?> GetPictureAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        await using var connection = GetConnection();
        return await ReadPictureAsync(connection, accountId, null, cancellationToken);
    }

    public async ValueTask<AccountPicture> GetOrCreateDefaultPictureAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var existing = await GetPictureAsync(accountId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var result = await SetDefaultPictureAsync(accountId, cancellationToken);
        if (!result.IsSuccess || result.Picture == null)
        {
            throw new InvalidOperationException("The default account picture is unavailable or invalid.");
        }

        return result.Picture;
    }

    public async ValueTask EnsurePictureAsync(
        string accountId,
        string? legacyPictureUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        if (await GetPictureAsync(accountId, cancellationToken) != null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(legacyPictureUrl))
        {
            var importResult = await TryImportPictureAsync(accountId, legacyPictureUrl, cancellationToken);
            if (importResult.IsSuccess)
            {
                return;
            }

            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "Failed to import legacy account picture for account {AccountId}. Error={Error}",
                    accountId,
                    importResult.Error);
            }
        }

        var defaultResult = await SetDefaultPictureAsync(accountId, cancellationToken);
        if (!defaultResult.IsSuccess && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(
                "Failed to assign default account picture for account {AccountId}. Error={Error}",
                accountId,
                defaultResult.Error);
        }
    }

    public async ValueTask<AccountPictureUpdateResult> SetPictureAsync(
        string accountId,
        Stream image,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(image);

        var readResult = await ReadLimitedAsync(image, cancellationToken);
        if (!readResult.IsSuccess)
        {
            return AccountPictureUpdateResult.Failure(readResult.Error);
        }

        return await SavePictureAsync(accountId, readResult.Bytes!, cancellationToken);
    }

    private async ValueTask<AccountPictureUpdateResult> TryImportPictureAsync(
        string accountId,
        string pictureUrl,
        CancellationToken cancellationToken)
    {
        if (!TryCreateSafeRemoteUri(pictureUrl, out var uri))
        {
            return AccountPictureUpdateResult.Failure(AccountPictureError.InvalidUrl);
        }

        if (!await IsPublicRemoteHostAsync(uri, cancellationToken))
        {
            return AccountPictureUpdateResult.Failure(AccountPictureError.InvalidUrl);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(m_Options.RemoteDownloadTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(PngContentType);
            request.Headers.Accept.ParseAdd(JpegContentType);

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                return AccountPictureUpdateResult.Failure(AccountPictureError.DownloadFailed);
            }

            if (!response.IsSuccessStatusCode)
            {
                return AccountPictureUpdateResult.Failure(AccountPictureError.DownloadFailed);
            }

            if (response.Content.Headers.ContentLength > m_Options.MaxBytes)
            {
                return AccountPictureUpdateResult.Failure(AccountPictureError.TooLarge);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var readResult = await ReadLimitedAsync(stream, timeout.Token);
            if (!readResult.IsSuccess)
            {
                return AccountPictureUpdateResult.Failure(readResult.Error);
            }

            return await SavePictureAsync(accountId, readResult.Bytes!, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AccountPictureUpdateResult.Failure(AccountPictureError.DownloadFailed);
        }
        catch (HttpRequestException)
        {
            return AccountPictureUpdateResult.Failure(AccountPictureError.DownloadFailed);
        }
        catch (IOException)
        {
            return AccountPictureUpdateResult.Failure(AccountPictureError.DownloadFailed);
        }
    }

    private async ValueTask<AccountPictureUpdateResult> SetDefaultPictureAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(m_Options.DefaultPicturePath);
            var readResult = await ReadLimitedAsync(stream, cancellationToken);
            if (!readResult.IsSuccess)
            {
                return AccountPictureUpdateResult.Failure(AccountPictureError.DefaultUnavailable);
            }

            return await SavePictureAsync(accountId, readResult.Bytes!, cancellationToken, overwriteExisting: false);
        }
        catch (IOException)
        {
            return AccountPictureUpdateResult.Failure(AccountPictureError.DefaultUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            return AccountPictureUpdateResult.Failure(AccountPictureError.DefaultUnavailable);
        }
    }

    private async ValueTask<AccountPictureUpdateResult> SavePictureAsync(
        string accountId,
        byte[] bytes,
        CancellationToken cancellationToken,
        bool overwriteExisting = true)
    {
        var validation = AccountPictureImageValidator.Validate(bytes, m_Options);
        if (!validation.IsSuccess)
        {
            return AccountPictureUpdateResult.Failure(validation.Error);
        }

        await using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var commandText = overwriteExisting
            ? @"
INSERT INTO `account_picture` (`account_id`, `content_type`, `image_bytes`, `width`, `height`)
VALUES (@accountId, @contentType, @imageBytes, @width, @height)
ON DUPLICATE KEY UPDATE
    `content_type` = VALUES(`content_type`),
    `image_bytes` = VALUES(`image_bytes`),
    `width` = VALUES(`width`),
    `height` = VALUES(`height`),
    `updated_at` = NOW();"
            : @"
INSERT INTO `account_picture` (`account_id`, `content_type`, `image_bytes`, `width`, `height`)
VALUES (@accountId, @contentType, @imageBytes, @width, @height)
ON DUPLICATE KEY UPDATE
    `account_id` = `account_id`;";

        var command = new CommandDefinition(
            commandText,
            new
            {
                accountId,
                contentType = validation.ContentType!,
                imageBytes = bytes,
                width = validation.Width,
                height = validation.Height
            },
            transaction: transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        if (overwriteExisting)
        {
            command = new CommandDefinition(
                "UPDATE `account` SET `updated_at` = NOW() WHERE `id` = @accountId;",
                new { accountId },
                transaction: transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
        }

        var picture = await ReadPictureAsync(connection, accountId, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return picture == null
            ? AccountPictureUpdateResult.Failure(AccountPictureError.DownloadFailed)
            : AccountPictureUpdateResult.Success(picture);
    }

    private async ValueTask<ReadImageResult> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var maxBytes = m_Options.MaxBytes;
        if (maxBytes <= 0)
        {
            return ReadImageResult.Failure(AccountPictureError.TooLarge);
        }

        var bufferSize = Math.Min(maxBytes, 64 * 1024);
        using var buffer = new MemoryStream(bufferSize);
        var rented = new byte[bufferSize];
        while (true)
        {
            var read = await stream.ReadAsync(rented, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                return ReadImageResult.Failure(AccountPictureError.TooLarge);
            }

            buffer.Write(rented, 0, read);
        }

        if (buffer.Length == 0)
        {
            return ReadImageResult.Failure(AccountPictureError.Empty);
        }

        return ReadImageResult.Success(buffer.ToArray());
    }

    private static async ValueTask<AccountPicture?> ReadPictureAsync(
        MySqlConnection connection,
        string accountId,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string QUERY = @"
SELECT
    `content_type` AS `ContentType`,
    `image_bytes` AS `Image`,
    `width` AS `Width`,
    `height` AS `Height`,
    `created_at` AS `CreatedAt`,
    `updated_at` AS `UpdatedAt`
FROM `account_picture`
WHERE `account_id` = @accountId;";

        var command = new CommandDefinition(
            QUERY,
            new { accountId },
            transaction: transaction,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountPicture>(command);
    }

    private static bool TryCreateSafeRemoteUri(string value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        if ((parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(parsedUri.UserInfo) ||
            string.IsNullOrWhiteSpace(parsedUri.Host) ||
            parsedUri.IsLoopback)
        {
            return false;
        }

        if (!parsedUri.IsDefaultPort &&
            parsedUri.Port != 80 &&
            parsedUri.Port != 443)
        {
            return false;
        }

        uri = parsedUri;
        return true;
    }

    private static async ValueTask<bool> IsPublicRemoteHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            IPAddress.Any.Equals(address) ||
            IPAddress.IPv6Any.Equals(address) ||
            IPAddress.None.Equals(address) ||
            IPAddress.IPv6None.Equals(address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 168 => false,
                198 when bytes[1] is 18 or 19 => false,
                >= 224 => false,
                _ => true
            };
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return !address.IsIPv6LinkLocal &&
                   !address.IsIPv6Multicast &&
                   !address.IsIPv6SiteLocal &&
                   !address.IsIPv6Teredo &&
                   bytes[0] is not 0xfc and not 0xfd;
        }

        return false;
    }

    private readonly record struct ReadImageResult(byte[]? Bytes, AccountPictureError Error)
    {
        public bool IsSuccess => Error == AccountPictureError.None && Bytes != null;

        public static ReadImageResult Success(byte[] bytes)
        {
            return new ReadImageResult(bytes, AccountPictureError.None);
        }

        public static ReadImageResult Failure(AccountPictureError error)
        {
            return new ReadImageResult(null, error);
        }
    }
}
