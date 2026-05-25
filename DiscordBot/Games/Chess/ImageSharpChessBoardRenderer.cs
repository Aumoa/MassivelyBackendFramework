using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DiscordBot.Games.Chess;

internal sealed class ImageSharpChessBoardRenderer : IChessBoardRenderer
{
    private const int SquareSize = 80;
    private const int BoardSize = SquareSize * 8;
    private const int Margin = 38;
    private const int ImageSize = BoardSize + Margin * 2;

    private static readonly Rgba32 Background = new(24, 28, 32);
    private static readonly Rgba32 LightSquare = new(233, 221, 203);
    private static readonly Rgba32 DarkSquare = new(103, 128, 95);
    private static readonly Rgba32 LastMoveSquare = new(230, 200, 75, 150);
    private static readonly Rgba32 LabelColor = new(230, 230, 220);
    private static readonly Rgba32 WhitePiece = new(246, 246, 238);
    private static readonly Rgba32 WhitePieceShadow = new(170, 170, 160);
    private static readonly Rgba32 BlackPiece = new(32, 34, 38);
    private static readonly Rgba32 BlackPieceHighlight = new(92, 96, 104);
    private static readonly Rgba32 Outline = new(12, 14, 16);

    public async ValueTask<byte[]> RenderAsync(
        ChessGameSession session,
        ChessSide perspective,
        CancellationToken cancellationToken = default)
    {
        using var image = new Image<Rgba32>(ImageSize, ImageSize, Background);
        var pieces = ParseFenPieces(session.Board.ToFen());
        var lastMove = session.MoveHistory.LastOrDefault();

        DrawBoard(image, perspective, lastMove);
        DrawLabels(image, perspective);

        foreach (var (square, piece) in pieces)
        {
            var (x, y) = SquareToTopLeft(square, perspective);
            DrawPiece(image, piece, x, y);
        }

        await using var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream, new PngEncoder(), cancellationToken);
        return stream.ToArray();
    }

    private static void DrawBoard(Image<Rgba32> image, ChessSide perspective, ChessMoveRecord? lastMove)
    {
        for (var rankIndex = 0; rankIndex < 8; rankIndex++)
        {
            for (var fileIndex = 0; fileIndex < 8; fileIndex++)
            {
                var x = Margin + fileIndex * SquareSize;
                var y = Margin + rankIndex * SquareSize;
                var color = (rankIndex + fileIndex) % 2 == 0 ? LightSquare : DarkSquare;
                FillRect(image, x, y, SquareSize, SquareSize, color);
            }
        }

        if (lastMove == null)
        {
            return;
        }

        DrawMoveHighlight(image, lastMove.FromSquare(), perspective);
        DrawMoveHighlight(image, lastMove.ToSquare(), perspective);
    }

    private static void DrawMoveHighlight(Image<Rgba32> image, string square, ChessSide perspective)
    {
        var (x, y) = SquareToTopLeft(square, perspective);
        BlendRect(image, x, y, SquareSize, SquareSize, LastMoveSquare);
    }

    private static void DrawLabels(Image<Rgba32> image, ChessSide perspective)
    {
        for (var i = 0; i < 8; i++)
        {
            var file = perspective == ChessSide.White
                ? (char)('a' + i)
                : (char)('h' - i);
            var rank = perspective == ChessSide.White
                ? (char)('8' - i)
                : (char)('1' + i);

            DrawText(image, file.ToString(), Margin + i * SquareSize + SquareSize / 2 - 7, ImageSize - Margin + 10, 3, LabelColor);
            DrawText(image, rank.ToString(), 12, Margin + i * SquareSize + SquareSize / 2 - 10, 3, LabelColor);
        }
    }

    private static void DrawPiece(Image<Rgba32> image, char piece, int x, int y)
    {
        var isWhite = char.IsUpper(piece);
        var fill = isWhite ? WhitePiece : BlackPiece;
        var accent = isWhite ? WhitePieceShadow : BlackPieceHighlight;
        var text = char.ToUpperInvariant(piece).ToString();
        var textColor = isWhite ? Outline : WhitePiece;

        var cx = x + SquareSize / 2;
        var baseY = y + 58;

        FillCircle(image, cx, y + 24, 12, fill);
        FillRect(image, cx - 18, y + 38, 36, 18, fill);
        FillRect(image, cx - 26, baseY, 52, 8, fill);
        FillRect(image, cx - 31, baseY + 8, 62, 7, accent);

        switch (char.ToLowerInvariant(piece))
        {
            case 'p':
                FillCircle(image, cx, y + 27, 13, fill);
                break;
            case 'n':
                FillRect(image, cx - 21, y + 18, 30, 34, fill);
                FillRect(image, cx - 6, y + 13, 24, 14, fill);
                DrawLine(image, cx - 20, y + 18, cx + 17, y + 13, 3, Outline);
                break;
            case 'b':
                DrawLine(image, cx - 13, y + 18, cx + 13, y + 42, 4, accent);
                DrawLine(image, cx + 13, y + 18, cx - 13, y + 42, 4, accent);
                break;
            case 'r':
                FillRect(image, cx - 24, y + 16, 48, 12, fill);
                FillRect(image, cx - 22, y + 12, 10, 10, fill);
                FillRect(image, cx - 5, y + 12, 10, 10, fill);
                FillRect(image, cx + 12, y + 12, 10, 10, fill);
                break;
            case 'q':
                FillCircle(image, cx - 18, y + 18, 7, fill);
                FillCircle(image, cx, y + 13, 8, fill);
                FillCircle(image, cx + 18, y + 18, 7, fill);
                DrawLine(image, cx - 18, y + 20, cx - 12, y + 43, 6, fill);
                DrawLine(image, cx, y + 18, cx, y + 43, 6, fill);
                DrawLine(image, cx + 18, y + 20, cx + 12, y + 43, 6, fill);
                break;
            case 'k':
                DrawLine(image, cx, y + 10, cx, y + 34, 5, accent);
                DrawLine(image, cx - 11, y + 20, cx + 11, y + 20, 5, accent);
                break;
        }

        DrawPieceOutline(image, cx, y);
        DrawText(image, text, cx - 8, y + 35, 3, textColor);
    }

    private static void DrawPieceOutline(Image<Rgba32> image, int cx, int y)
    {
        DrawLine(image, cx - 26, y + 66, cx + 26, y + 66, 2, Outline);
        DrawLine(image, cx - 31, y + 73, cx + 31, y + 73, 2, Outline);
    }

    private static Dictionary<string, char> ParseFenPieces(string fen)
    {
        var result = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
        var boardPart = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var ranks = boardPart.Split('/');

        for (var rankIndex = 0; rankIndex < ranks.Length; rankIndex++)
        {
            var rank = 8 - rankIndex;
            var file = 0;
            foreach (var c in ranks[rankIndex])
            {
                if (char.IsDigit(c))
                {
                    file += c - '0';
                    continue;
                }

                var square = $"{(char)('a' + file)}{rank}";
                result[square] = c;
                file++;
            }
        }

        return result;
    }

    private static (int X, int Y) SquareToTopLeft(string square, ChessSide perspective)
    {
        var file = square[0] - 'a';
        var rank = square[1] - '1';

        var displayFile = perspective == ChessSide.White ? file : 7 - file;
        var displayRank = perspective == ChessSide.White ? 7 - rank : rank;

        return (Margin + displayFile * SquareSize, Margin + displayRank * SquareSize);
    }

    private static void FillRect(Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color)
    {
        for (var yy = Math.Max(0, y); yy < Math.Min(image.Height, y + height); yy++)
        {
            for (var xx = Math.Max(0, x); xx < Math.Min(image.Width, x + width); xx++)
            {
                image[xx, yy] = color;
            }
        }
    }

    private static void BlendRect(Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color)
    {
        var alpha = color.A / 255f;
        for (var yy = Math.Max(0, y); yy < Math.Min(image.Height, y + height); yy++)
        {
            for (var xx = Math.Max(0, x); xx < Math.Min(image.Width, x + width); xx++)
            {
                image[xx, yy] = Blend(image[xx, yy], color, alpha);
            }
        }
    }

    private static void FillCircle(Image<Rgba32> image, int cx, int cy, int radius, Rgba32 color)
    {
        var radiusSquared = radius * radius;
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
                {
                    continue;
                }

                var dx = x - cx;
                var dy = y - cy;
                if (dx * dx + dy * dy <= radiusSquared)
                {
                    image[x, y] = color;
                }
            }
        }
    }

    private static void DrawLine(Image<Rgba32> image, int x0, int y0, int x1, int y1, int thickness, Rgba32 color)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;

        while (true)
        {
            FillCircle(image, x0, y0, Math.Max(1, thickness / 2), color);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static void DrawText(Image<Rgba32> image, string text, int x, int y, int scale, Rgba32 color)
    {
        var cursor = x;
        foreach (var c in text)
        {
            if (c == ' ')
            {
                cursor += 6 * scale;
                continue;
            }

            if (Font.TryGetValue(char.ToUpperInvariant(c), out var glyph))
            {
                DrawGlyph(image, glyph, cursor, y, scale, color);
            }

            cursor += 6 * scale;
        }
    }

    private static void DrawGlyph(Image<Rgba32> image, string[] glyph, int x, int y, int scale, Rgba32 color)
    {
        for (var row = 0; row < glyph.Length; row++)
        {
            for (var col = 0; col < glyph[row].Length; col++)
            {
                if (glyph[row][col] == '1')
                {
                    FillRect(image, x + col * scale, y + row * scale, scale, scale, color);
                }
            }
        }
    }

    private static Rgba32 Blend(Rgba32 background, Rgba32 foreground, float alpha)
    {
        return new Rgba32(
            (byte)(background.R * (1 - alpha) + foreground.R * alpha),
            (byte)(background.G * (1 - alpha) + foreground.G * alpha),
            (byte)(background.B * (1 - alpha) + foreground.B * alpha),
            255);
    }

    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['C'] = ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01111", "10000", "10000", "10011", "10001", "10001", "01111"],
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['N'] = ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
        ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    };
}

internal static class ChessMoveRecordExtensions
{
    public static string FromSquare(this ChessMoveRecord move) => move.Uci[..2];

    public static string ToSquare(this ChessMoveRecord move) => move.Uci[2..4];
}
