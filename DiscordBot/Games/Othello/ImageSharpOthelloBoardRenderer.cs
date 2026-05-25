using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DiscordBot.Games.Othello;

internal sealed class ImageSharpOthelloBoardRenderer(IOthelloEngine engine) : IOthelloBoardRenderer
{
    private const int SquareSize = 80;
    private const int BoardSize = SquareSize * 8;
    private const int Margin = 38;
    private const int ImageSize = BoardSize + Margin * 2;

    private static readonly Rgba32 Background = new(22, 28, 27);
    private static readonly Rgba32 BoardLine = new(16, 70, 54);
    private static readonly Rgba32 BoardGreen = new(36, 128, 89);
    private static readonly Rgba32 LastMoveSquare = new(238, 202, 74, 145);
    private static readonly Rgba32 LegalDot = new(238, 238, 220, 145);
    private static readonly Rgba32 LabelColor = new(230, 230, 220);
    private static readonly Rgba32 BlackDisc = new(18, 20, 22);
    private static readonly Rgba32 BlackDiscHighlight = new(68, 72, 76);
    private static readonly Rgba32 WhiteDisc = new(244, 241, 230);
    private static readonly Rgba32 WhiteDiscShadow = new(170, 164, 150);
    private static readonly Rgba32 DiscOutline = new(8, 10, 12);

    public async ValueTask<byte[]> RenderAsync(
        OthelloGameSession session,
        CancellationToken cancellationToken = default)
    {
        using var image = new Image<Rgba32>(ImageSize, ImageSize, Background);
        var lastMove = session.MoveHistory.LastOrDefault(move => !move.IsPass);
        var legalMoves = session.IsActive
            ? engine.GetLegalMoves(session.Board).Select(move => move.Coordinate).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        DrawBoard(image, lastMove);
        DrawLabels(image);
        DrawLegalMoveHints(image, legalMoves);
        DrawDiscs(image, session.Board);

        await using var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream, new PngEncoder(), cancellationToken);
        return stream.ToArray();
    }

    private static void DrawBoard(Image<Rgba32> image, OthelloMoveRecord? lastMove)
    {
        FillRect(image, Margin, Margin, BoardSize, BoardSize, BoardGreen);

        for (var i = 0; i <= 8; i++)
        {
            var offset = Margin + i * SquareSize;
            DrawLine(image, Margin, offset, Margin + BoardSize, offset, 2, BoardLine);
            DrawLine(image, offset, Margin, offset, Margin + BoardSize, 2, BoardLine);
        }

        foreach (var point in new[] { "c3", "f3", "c6", "f6" })
        {
            var (x, y) = SquareCenter(point);
            FillCircle(image, x, y, 5, BoardLine);
        }

        if (lastMove != null)
        {
            var (file, rank) = FromCoordinate(lastMove.Coordinate);
            var x = Margin + file * SquareSize;
            var y = Margin + (7 - rank) * SquareSize;
            BlendRect(image, x, y, SquareSize, SquareSize, LastMoveSquare);
        }
    }

    private static void DrawLabels(Image<Rgba32> image)
    {
        for (var i = 0; i < 8; i++)
        {
            var file = (char)('a' + i);
            var rank = (char)('8' - i);
            DrawText(image, file.ToString(), Margin + i * SquareSize + SquareSize / 2 - 7, ImageSize - Margin + 10, 3, LabelColor);
            DrawText(image, rank.ToString(), 12, Margin + i * SquareSize + SquareSize / 2 - 10, 3, LabelColor);
        }
    }

    private static void DrawLegalMoveHints(Image<Rgba32> image, IReadOnlySet<string> legalMoves)
    {
        foreach (var coordinate in legalMoves)
        {
            var (x, y) = SquareCenter(coordinate);
            FillCircle(image, x, y, 9, LegalDot);
        }
    }

    private static void DrawDiscs(Image<Rgba32> image, OthelloBoard board)
    {
        for (var file = 0; file < 8; file++)
        {
            for (var rank = 0; rank < 8; rank++)
            {
                var disc = board.Cells[file, rank];
                if (disc == OthelloDisc.Empty)
                {
                    continue;
                }

                var coordinate = ToCoordinate(file, rank);
                var (x, y) = SquareCenter(coordinate);
                DrawDisc(image, x, y, disc);
            }
        }
    }

    private static void DrawDisc(Image<Rgba32> image, int cx, int cy, OthelloDisc disc)
    {
        var fill = disc == OthelloDisc.Black ? BlackDisc : WhiteDisc;
        var accent = disc == OthelloDisc.Black ? BlackDiscHighlight : WhiteDiscShadow;
        FillCircle(image, cx + 3, cy + 4, 29, new Rgba32(0, 0, 0, 70));
        FillCircle(image, cx, cy, 30, DiscOutline);
        FillCircle(image, cx, cy, 27, fill);
        FillCircle(image, cx - 9, cy - 9, 7, accent);
    }

    private static (int X, int Y) SquareCenter(string coordinate)
    {
        var (file, rank) = FromCoordinate(coordinate);
        return (
            Margin + file * SquareSize + SquareSize / 2,
            Margin + (7 - rank) * SquareSize + SquareSize / 2);
    }

    private static (int File, int Rank) FromCoordinate(string coordinate) => (coordinate[0] - 'a', coordinate[1] - '1');

    private static string ToCoordinate(int file, int rank) => $"{(char)('a' + file)}{rank + 1}";

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
    };
}
