// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Micro QR Code, ISO/IEC 18004, and Rectangular Micro QR Code, ISO/IEC 23941, whose single finder pattern sits in the upper left corner. The symbol
/// lies in one of the four quadrants of the finder pattern, along the axes of the image. For each quadrant the edges of the finder pattern give the module
/// size, and the top row and the left column, a timing pattern with no two light modules side by side, run to the light quiet zone and give the size.
/// </summary>
internal sealed class MicroQrReader : MatrixReader
{
    /// <summary>
    /// The light run in modules that ends the top row or the left column: the quiet zone is two modules.
    /// </summary>
    private const float QuietRun = 1.5F;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.MicroQrCode | BarcodeFamily.RectangularMicroQrCode;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        QrFinderState state = context.State<QrFinderState>();
        foreach (FinderPattern pattern in state.Patterns(context))
        {
            if (!state.IsUsed(pattern) && TryRead(context, pattern))
            {
                state.Use(pattern);
            }
        }
    }

    /// <summary>
    /// Tries the quadrants of a finder pattern, each read as written and mirrored.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="pattern">The finder pattern.</param>
    /// <returns><see langword="true"/> when a symbol decodes.</returns>
    private static bool TryRead(DecodeContext context, FinderPattern pattern)
    {
        BitImage bits = context.Bits;
        ReadOnlySpan<int> signs = [1, 1, -1, 1, -1, -1, 1, -1];
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            int dx = signs[quadrant * 2];
            int dy = signs[(quadrant * 2) + 1];
            for (int mirror = 0; mirror < 2; mirror++)
            {
                // Along u the columns count up and along v the rows. The symbol as written turns u into v clockwise, so u lies along x when the quadrant is
                // the upper left or the lower right one.
                bool alongX = (dx * dy > 0) == (mirror == 0);
                (int ux, int uy) = alongX ? (dx, 0) : (0, dy);
                (int vx, int vy) = alongX ? (0, dy) : (dx, 0);
                if (TryReadQuadrant(context, bits, pattern, ux, uy, vx, vy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Measures, samples and decodes the symbol of a finder pattern in one quadrant.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image.</param>
    /// <param name="pattern">The finder pattern.</param>
    /// <param name="ux">The x step along a row of the symbol.</param>
    /// <param name="uy">The y step along a row of the symbol.</param>
    /// <param name="vx">The x step along a column of the symbol.</param>
    /// <param name="vy">The y step along a column of the symbol.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryReadQuadrant(DecodeContext context, BitImage bits, FinderPattern pattern, int ux, int uy, int vx, int vy)
    {
        // The outer edges of the finder pattern on all four sides: it is 7 modules across.
        float backU = Edge(bits, pattern.X, pattern.Y, -ux, -uy);
        float aheadU = Edge(bits, pattern.X, pattern.Y, ux, uy);
        float backV = Edge(bits, pattern.X, pattern.Y, -vx, -vy);
        float aheadV = Edge(bits, pattern.X, pattern.Y, vx, vy);
        if (float.IsNaN(backU) || float.IsNaN(aheadU) || float.IsNaN(backV) || float.IsNaN(aheadV))
        {
            return false;
        }

        float moduleU = (backU + aheadU) / 7F;
        float moduleV = (backV + aheadV) / 7F;
        float originX = pattern.X - (backU * ux) - (backV * vx);
        float originY = pattern.Y - (backU * uy) - (backV * vy);

        // The top row and the left column, along their centre lines.
        float width = Extent(bits, originX + (0.5F * moduleV * vx), originY + (0.5F * moduleV * vy), ux, uy, moduleU);
        float height = Extent(bits, originX + (0.5F * moduleU * ux), originY + (0.5F * moduleU * uy), vx, vy, moduleV);
        int columns = (int)MathF.Round(width / moduleU);
        int rows = (int)MathF.Round(height / moduleV);
        if (!TryFindSize(rows, columns, out QrEncoder.SymbolSize size))
        {
            return false;
        }

        BarcodeFamily family = size.Format == QrFormat.Micro ? BarcodeFamily.MicroQrCode : BarcodeFamily.RectangularMicroQrCode;
        if (!context.Wants(family))
        {
            return false;
        }

        PointF origin = new(originX, originY);
        PointF right = new(originX + (width * ux), originY + (width * uy));
        PointF down = new(originX + (height * vx), originY + (height * vy));
        PointF corner = new(right.X + (height * vx), right.Y + (height * vy));
        ReadOnlySpan<PointF> from = [new(0, 0), new(columns, 0), new(columns, rows), new(0, rows)];
        ReadOnlySpan<PointF> to = [origin, right, corner, down];
        PerspectiveTransform transform = PerspectiveTransform.QuadrilateralToQuadrilateral(from, to);
        if (!ModuleMatrix.TrySample(bits, transform, rows, columns, out ModuleMatrix matrix)
            || !QrSymbolDecoder.TryDecode(matrix, size, out string text, out string identifier))
        {
            return false;
        }

        context.Add(family, text, identifier, [origin, right, corner, down]);
        return true;
    }

    /// <summary>
    /// Returns the Micro QR Code or Rectangular Micro QR Code size of a module count.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="columns">The columns.</param>
    /// <param name="size">The size.</param>
    /// <returns><see langword="true"/> when a size has the count.</returns>
    private static bool TryFindSize(int rows, int columns, out QrEncoder.SymbolSize size)
    {
        // Section 5.3.1: a Micro QR Code symbol is 11 modules on a side for M1 in steps of 2.
        if (rows == columns && rows is 11 or 13 or 15 or 17)
        {
            return QrEncoder.TryGetSize(QrFormat.Micro, (rows - 9) / 2, out size);
        }

        for (int version = 1; QrEncoder.TryGetSize(QrFormat.Rectangular, version, out size); version++)
        {
            if (size.Rows == rows && size.Columns == columns)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the distance from the centre of a finder pattern to its outer edge in one direction: past the dark centre, the light ring and the dark ring.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="x">The x coordinate of the centre.</param>
    /// <param name="y">The y coordinate of the centre.</param>
    /// <param name="dx">The x step.</param>
    /// <param name="dy">The y step.</param>
    /// <returns>The distance in pixels, or NaN.</returns>
    private static float Edge(BitImage bits, float x, float y, int dx, int dy)
    {
        int px = (int)x;
        int py = (int)y;
        int phase = 0;
        while (px >= 0 && py >= 0 && px < bits.Width && py < bits.Height)
        {
            bool dark = bits[px, py];
            if (phase == 0 && !dark)
            {
                phase = 1;
            }
            else if (phase == 1 && dark)
            {
                phase = 2;
            }
            else if (phase == 2 && !dark)
            {
                break;
            }

            px += dx;
            py += dy;
        }

        if (phase != 2)
        {
            return float.NaN;
        }

        // The edge is the near side of the first light pixel past the ring.
        float edge = dx != 0 ? (dx > 0 ? px : px + 1) : (dy > 0 ? py : py + 1);
        return MathF.Abs(edge - (dx != 0 ? x : y));
    }

    /// <summary>
    /// Returns the length of a line of modules from an edge of the symbol to the last dark pixel before a light run of the quiet zone.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="x">The x coordinate of the start, on the edge.</param>
    /// <param name="y">The y coordinate of the start, on the edge.</param>
    /// <param name="dx">The x step.</param>
    /// <param name="dy">The y step.</param>
    /// <param name="module">The module size in pixels along the line.</param>
    /// <returns>The length in pixels.</returns>
    private static float Extent(BitImage bits, float x, float y, int dx, int dy, float module)
    {
        // The first pixel inside the symbol.
        int px = (int)MathF.Floor(x + (0.5F * dx));
        int py = (int)MathF.Floor(y + (0.5F * dy));
        float end = dx != 0 ? x : y;
        int light = 0;
        while (px >= 0 && py >= 0 && px < bits.Width && py < bits.Height)
        {
            if (bits[px, py])
            {
                // The far side of the dark pixel.
                end = dx > 0 ? px + 1 : dx < 0 ? px : dy > 0 ? py + 1 : py;
                light = 0;
            }
            else if (++light > module * QuietRun)
            {
                break;
            }

            px += dx;
            py += dy;
        }

        return MathF.Abs(end - (dx != 0 ? x : y));
    }
}
