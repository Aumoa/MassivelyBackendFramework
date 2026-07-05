using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using AI;
using DiscordBot.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

internal sealed partial class DiscordWebPageTools(
    IHttpClientFactory httpClientFactory,
    IWebPageAddressResolver addressResolver,
    IOptions<WebPageReadOptions> options,
    ILogger<DiscordWebPageTools> logger)
{
    private const int BufferSize = 81920;
    private const int HardMaxBytes = 2_000_000;
    private const int HardMaxCharacters = 60_000;
    private const int MinCharacters = 1_000;

    [ToolFunction(
        Name = "read_static_web_page",
        Description = @"공개 HTTP/HTTPS 웹페이지의 정적 텍스트를 제한적으로 읽습니다.

이 도구는 브라우저가 아니라 HTTP GET으로 서버가 처음 반환한 HTML/텍스트에서 내용을 추출합니다. JavaScript 실행, 로그인, 쿠키, 버튼 클릭, 스크롤, 동영상/이미지 분석은 지원하지 않습니다.
사용자가 뉴스, 문서, 블로그, 공지, 일반 웹페이지 URL을 주며 내용을 확인/요약/분석해 달라고 할 때만 사용하세요.
Discord 메시지 URL은 이 도구가 아니라 채팅 조회 도구를 사용하세요.
최종 답변에서는 반드시 '가져온 정적 텍스트 기준'이라고 밝혀 사용자가 브라우저 렌더링 전체를 확인한 것으로 오해하지 않게 하세요.
동적 렌더링 페이지처럼 내용이 부족하면 읽을 수 없다고 말하고, 본문을 붙여 달라고 안내하세요.")]
    public async Task<string> ReadStaticWebPageAsync(
        [ToolParameterInfo(Description = "읽을 공개 HTTP/HTTPS 웹페이지 URL입니다.")]
        string url,
        [ToolParameterInfo(Description = "응답에 포함할 추출 텍스트 최대 글자 수입니다. 기본 12000, 최대 60000입니다.")]
        int max_characters = 0,
        CancellationToken cancellationToken = default)
    {
        if (!TryCreateRequestUri(url, out var uri, out var error))
        {
            return error;
        }

        var currentOptions = options.Value;
        var maxBytes = NormalizeRange(currentOptions.MaxBytes, 1_000_000, 16_384, HardMaxBytes);
        var maxCharacters = NormalizeRange(
            max_characters,
            currentOptions.DefaultMaxCharacters,
            MinCharacters,
            NormalizeRange(currentOptions.MaxCharacters, 50_000, MinCharacters, HardMaxCharacters));
        var maxRedirects = NormalizeRange(currentOptions.MaxRedirects, 5, 0, 10);
        var httpClient = httpClientFactory.CreateClient(WebPageReadOptions.HttpClientName);

        try
        {
            for (var redirectCount = 0; ; redirectCount++)
            {
                var validationError = await WebPageAddressSafety.GetPublicUriValidationErrorAsync(
                    uri,
                    addressResolver,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    return $"정적 웹페이지 텍스트를 읽지 못했습니다. 공개 HTTP/HTTPS 웹페이지만 지원합니다: {validationError}";
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd(
                    "text/html, application/xhtml+xml, text/plain, application/json;q=0.8, application/xml;q=0.8, text/xml;q=0.8");
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= maxRedirects)
                    {
                        return $"정적 웹페이지 텍스트를 읽지 못했습니다. 리다이렉트가 {maxRedirects}회를 초과했습니다.";
                    }

                    if (!TryCreateRedirectUri(uri, response.Headers.Location, out var redirectUri, out var redirectError))
                    {
                        return redirectError;
                    }

                    uri = redirectUri;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return $"정적 웹페이지 텍스트를 읽지 못했습니다. HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                }

                var contentType = response.Content.Headers.ContentType;
                var mediaType = contentType?.MediaType;
                if (!IsSupportedMediaType(mediaType))
                {
                    return $"정적 웹페이지 텍스트를 읽지 못했습니다. 텍스트/HTML 콘텐츠로 보이지 않습니다: {mediaType ?? "(unknown)"}";
                }

                var (bytes, readError) = await ReadContentBytesAsync(response.Content, maxBytes, cancellationToken);
                if (!string.IsNullOrWhiteSpace(readError) || bytes == null)
                {
                    return readError ?? "정적 웹페이지 본문을 읽지 못했습니다.";
                }

                var decoded = DecodeContent(bytes, contentType);
                var readableText = ExtractReadableText(decoded, mediaType);
                if (string.IsNullOrWhiteSpace(readableText))
                {
                    return "정적 웹페이지 텍스트를 읽지 못했습니다. 서버가 처음 반환한 HTML/텍스트에서 읽을 만한 본문을 추출하지 못했습니다. JavaScript 렌더링, 로그인, 이미지/동영상 중심 페이지일 수 있으니 필요한 본문을 직접 붙여 주세요.";
                }

                var truncatedText = Truncate(readableText, maxCharacters, out var truncated);
                var contentWarning = BuildContentWarning(decoded, readableText, mediaType);
                logger.LogInformation(
                    "Read static web page {Url}: {CharacterCount} characters extracted.",
                    uri,
                    readableText.Length);

                return $"""
정적 웹페이지 텍스트 읽기 결과
Source: {uri}
Fetch-Mode: static HTTP GET; JavaScript was not executed.
Content-Type: {mediaType ?? "(unknown)"}
Characters: {readableText.Length}{(truncated ? $" (truncated to {maxCharacters})" : string.Empty)}
{contentWarning}

응답 규칙:
- 이 결과는 브라우저 렌더링 전체가 아니라 서버가 처음 반환한 HTML/텍스트에서 추출한 내용입니다.
- 최종 답변에 "가져온 정적 텍스트 기준"이라고 밝혀 주세요.
- 내용이 비어 있거나 질문에 필요한 본문이 부족하면 웹페이지를 실제로 확인했다고 단정하지 말고, JavaScript 렌더링/권한/비텍스트 콘텐츠 때문에 읽지 못했을 수 있다고 안내하세요.

{truncatedText}
""";
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "정적 웹페이지 텍스트 요청 시간이 초과되었습니다.";
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning(e, "Failed to read static web page {Url}.", uri);
            return $"정적 웹페이지 텍스트를 읽지 못했습니다: {e.Message}";
        }
    }

    private static bool TryCreateRequestUri(
        string url,
        out Uri uri,
        out string error)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "읽을 정적 웹페이지 URL이 필요합니다.";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            error = "올바른 절대 URL이 아닙니다.";
            return false;
        }

        uri = parsed;
        error = string.Empty;
        return true;
    }

    private static bool TryCreateRedirectUri(
        Uri currentUri,
        Uri? location,
        out Uri redirectUri,
        out string error)
    {
        redirectUri = null!;
        if (location == null)
        {
            error = "정적 웹페이지 요청 중 Location 헤더 없는 리다이렉트가 반환되었습니다.";
            return false;
        }

        if (!Uri.TryCreate(currentUri, location, out var parsed))
        {
            error = "정적 웹페이지 리다이렉트 URL을 해석하지 못했습니다.";
            return false;
        }

        redirectUri = parsed;
        error = string.Empty;
        return true;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 301 or 302 or 303 or 307 or 308;
    }

    private static bool IsSupportedMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return true;
        }

        var normalized = mediaType.Trim().ToLowerInvariant();
        return normalized.StartsWith("text/", StringComparison.Ordinal)
               || normalized is "application/html"
                   or "application/xhtml+xml"
                   or "application/xml"
                   or "application/json"
                   or "application/ld+json"
                   or "application/rss+xml"
                   or "application/atom+xml";
    }

    private static async Task<(byte[]? Bytes, string? Error)> ReadContentBytesAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var contentLength = content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
        {
            return (null, $"정적 웹페이지 본문이 {contentLength.Value} bytes로 제한({maxBytes} bytes)을 초과합니다.");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long totalBytes = 0;
        try
        {
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maxBytes)
                {
                    return (null, $"정적 웹페이지 다운로드가 제한({maxBytes} bytes)을 초과했습니다.");
                }

                output.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return (output.ToArray(), null);
    }

    private static string DecodeContent(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        var charset = contentType?.CharSet?.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset).GetString(bytes).Trim('\uFEFF');
            }
            catch (ArgumentException)
            {
            }
        }

        return Encoding.UTF8.GetString(bytes).Trim('\uFEFF');
    }

    internal static string ExtractReadableText(string content, string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalizedMediaType = mediaType?.Trim().ToLowerInvariant();
        if (normalizedMediaType is null
            || normalizedMediaType.Contains("html", StringComparison.Ordinal))
        {
            return ExtractHtmlText(content);
        }

        return NormalizeText(content);
    }

    private static string ExtractHtmlText(string html)
    {
        var text = ScriptStyleRegex().Replace(html, " ");
        text = HtmlCommentRegex().Replace(text, " ");
        text = HtmlBlockRegex().Replace(text, "\n");
        text = HtmlTagRegex().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        return NormalizeText(text);
    }

    private static string NormalizeText(string value)
    {
        var text = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        text = HorizontalWhitespaceRegex().Replace(text, " ");
        text = ExcessiveNewLineRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    private static string Truncate(string value, int maxCharacters, out bool truncated)
    {
        if (value.Length <= maxCharacters)
        {
            truncated = false;
            return value;
        }

        truncated = true;
        return value[..maxCharacters].TrimEnd() + "\n\n...(truncated)";
    }

    private static string BuildContentWarning(string decodedContent, string readableText, string? mediaType)
    {
        var normalizedMediaType = mediaType?.Trim().ToLowerInvariant();
        if (normalizedMediaType != null && !normalizedMediaType.Contains("html", StringComparison.Ordinal))
        {
            return "Content-Warning: none";
        }

        if (readableText.Length < 500
            && decodedContent.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            return "Content-Warning: extracted text is short and the page includes scripts; the main content may require JavaScript rendering.";
        }

        return "Content-Warning: static HTML text only; client-rendered content may be missing.";
    }

    private static int NormalizeRange(int value, int fallback, int min, int max)
    {
        if (max < min)
        {
            max = min;
        }

        if (value <= 0)
        {
            value = fallback;
        }

        return Math.Clamp(value, min, max);
    }

    [GeneratedRegex(@"<(script|style|noscript|svg|canvas|template)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"</?(?:article|aside|blockquote|br|dd|div|dl|dt|figcaption|figure|footer|h[1-6]|header|hr|li|main|nav|ol|p|pre|section|table|tbody|td|tfoot|th|thead|tr|ul)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlBlockRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[ \t\f\v\u00a0]+", RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessiveNewLineRegex();
}
