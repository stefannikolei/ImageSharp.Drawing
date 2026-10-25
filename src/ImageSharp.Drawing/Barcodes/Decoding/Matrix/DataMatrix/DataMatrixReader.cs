// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Data Matrix ECC 200, ISO/IEC 16022, with the rectangular extension of ISO/IEC 21471. A symbol is a region of <see cref="SymbolRegions"/>: the dark
/// point nearest each corner of the region is a corner of the symbol, and the two sides that are most dark form the solid L of the finder pattern, whose
/// corner is the lower left of the symbol. The alternating timing patterns of the two other sides count the modules. The placement of
/// <see cref="DataMatrixEncoder.CodewordPositions"/> reads the codewords, the Reed-Solomon code over GF(256) corrects each block, and
/// <see cref="DataMatrixBitstream"/> decodes the data.
/// </summary>
internal sealed class DataMatrixReader : MatrixReader
{
    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.DataMatrix;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        BitImage bits = context.Bits;
        foreach (Rectangle region in context.State<SymbolRegions>().Find(context, 1))
        {
            TryRead(context, bits, region);
        }
    }

    /// <summary>
    /// Finds and decodes the symbol of a region.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> when a symbol decodes.</returns>
    private static bool TryRead(DecodeContext context, BitImage bits, Rectangle region)
    {
        // The dark pixels nearest the corners of the region, as the outer corners of their pixels, in the order upper left, upper right, lower right, lower
        // left.
        Span<PointF> corners = stackalloc PointF[4];
        for (int c = 0; c < 4; c++)
        {
            if (!TryFindCorner(bits, region, c, out corners[c]))
            {
                return false;
            }
        }

        // The solid sides of the L are dark almost all along. They meet at the lower left corner of the symbol.
        Span<float> darkness = stackalloc float[4];
        for (int side = 0; side < 4; side++)
        {
            darkness[side] = Darkness(bits, corners[side], corners[(side + 1) % 4], corners);
        }

        int best = 0;
        for (int side = 1; side < 4; side++)
        {
            if (darkness[side] + darkness[(side + 3) % 4] > darkness[best] + darkness[(best + 3) % 4])
            {
                best = side;
            }
        }

        // Side best runs from corner best, and the side before it ends there, so corner best is the corner of the L.
        if (darkness[best] < 0.8F || darkness[(best + 3) % 4] < 0.8F)
        {
            return false;
        }

        PointF bottomLeft = corners[best];
        PointF a = corners[(best + 1) % 4];
        PointF b = corners[(best + 3) % 4];
        for (int mirror = 0; mirror < 2; mirror++)
        {
            // The symbol as written has its upper left clockwise of its lower right about the lower left, in image coordinates with y down.
            float cross = ((a.X - bottomLeft.X) * (b.Y - bottomLeft.Y)) - ((a.Y - bottomLeft.Y) * (b.X - bottomLeft.X));
            (PointF topLeft, PointF bottomRight) = (cross > 0) == (mirror == 0) ? (a, b) : (b, a);
            if (TryReadSymbol(context, bits, topLeft, bottomLeft, bottomRight))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Counts the modules of a symbol from its L and its timing patterns, then samples and decodes it.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image.</param>
    /// <param name="topLeft">The upper left corner.</param>
    /// <param name="bottomLeft">The lower left corner.</param>
    /// <param name="bottomRight">The lower right corner.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryReadSymbol(DecodeContext context, BitImage bits, PointF topLeft, PointF bottomLeft, PointF bottomRight)
    {
        PointF topRight = new(topLeft.X + bottomRight.X - bottomLeft.X, topLeft.Y + bottomRight.Y - bottomLeft.Y);
        float module = ModuleSize(bits, bottomLeft, topLeft, bottomRight);
        if (module < 0.8F)
        {
            return false;
        }

        // The timing pattern of the top row is dark in every even column and that of the right column in every odd row, so each has half as many dark modules
        // as the side has modules.
        int columns = 2 * DarkRuns(bits, topLeft, topRight, bottomLeft, module);
        int rows = 2 * DarkRuns(bits, bottomRight, topRight, bottomLeft, module);
        if (!DataMatrixEncoder.TryGetSize(rows, columns, out DataMatrixEncoder.DataMatrixSize size))
        {
            return false;
        }

        ReadOnlySpan<PointF> from = [new(0, 0), new(columns, 0), new(columns, rows), new(0, rows)];
        ReadOnlySpan<PointF> to = [topLeft, topRight, bottomRight, bottomLeft];
        PerspectiveTransform transform = PerspectiveTransform.QuadrilateralToQuadrilateral(from, to);
        if (!ModuleMatrix.TrySample(bits, transform, rows, columns, out ModuleMatrix matrix)
            || !TryDecode(matrix, size, out string text, out string identifier))
        {
            return false;
        }

        context.Add(BarcodeFamily.DataMatrix, text, identifier, [topLeft, topRight, bottomRight, bottomLeft]);
        return true;
    }

    /// <summary>
    /// Finds the dark pixel nearest a corner of a region, along the diagonals that sweep in from the corner.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <param name="corner">The corner: 0 upper left, 1 upper right, 2 lower right, 3 lower left.</param>
    /// <param name="point">The outer corner of the pixel.</param>
    /// <returns><see langword="true"/> when the region has a dark pixel.</returns>
    private static bool TryFindCorner(BitImage bits, Rectangle region, int corner, out PointF point)
    {
        int right = corner is 1 or 2 ? 1 : 0;
        int down = corner is 2 or 3 ? 1 : 0;
        int startX = right == 1 ? region.Right - 1 : region.Left;
        int startY = down == 1 ? region.Bottom - 1 : region.Top;
        int stepX = right == 1 ? -1 : 1;
        int stepY = down == 1 ? -1 : 1;
        int limit = region.Width + region.Height;
        for (int distance = 0; distance < limit; distance++)
        {
            // The pixels at this distance from the corner, from the middle of the diagonal outwards, so a square corner finds its corner pixel first.
            for (int offset = 0; offset <= distance + 1; offset++)
            {
                int i = (distance / 2) + ((offset & 1) == 0 ? offset / 2 : -((offset + 1) / 2));
                if (i < 0 || i > distance)
                {
                    continue;
                }

                int x = startX + (stepX * i);
                int y = startY + (stepY * (distance - i));
                if (x >= region.Left && x < region.Right && y >= region.Top && y < region.Bottom && bits[x, y])
                {
                    point = new PointF(x + right, y + down);
                    return true;
                }
            }
        }

        point = default;
        return false;
    }

    /// <summary>
    /// Returns the share of dark pixels along a side of the symbol, half a pixel inside it.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="from">The start of the side.</param>
    /// <param name="to">The end of the side.</param>
    /// <param name="corners">The four corners, whose centre gives the inside.</param>
    /// <returns>The share, 0 to 1.</returns>
    private static float Darkness(BitImage bits, PointF from, PointF to, ReadOnlySpan<PointF> corners)
    {
        PointF centre = new((corners[0].X + corners[1].X + corners[2].X + corners[3].X) / 4, (corners[0].Y + corners[1].Y + corners[2].Y + corners[3].Y) / 4);
        PointF inward = Towards(new PointF((from.X + to.X) / 2, (from.Y + to.Y) / 2), centre, 0.5F);
        int samples = Math.Max(2, (int)MathF.Ceiling(Length(from, to)));
        int dark = 0;
        for (int k = 0; k < samples; k++)
        {
            float t = (k + 0.5F) / samples;
            if (IsDark(bits, from.X + ((to.X - from.X) * t) + inward.X, from.Y + ((to.Y - from.Y) * t) + inward.Y))
            {
                dark++;
            }
        }

        return dark / (float)samples;
    }

    /// <summary>
    /// Returns the module size from the solid sides: the shortest dark run across them, inwards from the edge, at points along each.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="bottomLeft">The corner of the L.</param>
    /// <param name="topLeft">The end of the left side.</param>
    /// <param name="bottomRight">The end of the bottom side.</param>
    /// <returns>The module size in pixels.</returns>
    private static float ModuleSize(BitImage bits, PointF bottomLeft, PointF topLeft, PointF bottomRight)
    {
        float shortest = float.MaxValue;
        for (int side = 0; side < 2; side++)
        {
            PointF end = side == 0 ? topLeft : bottomRight;
            PointF other = side == 0 ? bottomRight : topLeft;
            PointF across = Towards(bottomLeft, other, 1F);
            for (int k = 1; k < 16; k++)
            {
                float t = k / 16F;
                float x = bottomLeft.X + ((end.X - bottomLeft.X) * t);
                float y = bottomLeft.Y + ((end.Y - bottomLeft.Y) * t);
                float run = 0;
                while (run < 64 && IsDark(bits, x + (across.X * (run + 0.5F)), y + (across.Y * (run + 0.5F))))
                {
                    run++;
                }

                if (run > 0)
                {
                    shortest = MathF.Min(shortest, run);
                }
            }
        }

        return shortest == float.MaxValue ? 0 : shortest;
    }

    /// <summary>
    /// Counts the dark runs along a timing pattern, half a module inside the side and half a module from its ends.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="from">The start of the side.</param>
    /// <param name="to">The end of the side.</param>
    /// <param name="opposite">A corner on the far side, which gives the inside.</param>
    /// <param name="module">The module size in pixels.</param>
    /// <returns>The number of dark runs.</returns>
    private static int DarkRuns(BitImage bits, PointF from, PointF to, PointF opposite, float module)
    {
        PointF inward = Towards(from, opposite, module / 2);
        PointF along = Towards(from, to, module / 2);
        PointF start = new(from.X + inward.X + along.X, from.Y + inward.Y + along.Y);
        PointF end = new(to.X + inward.X - along.X, to.Y + inward.Y - along.Y);
        int samples = Math.Max(2, (int)MathF.Ceiling(Length(start, end) * 2));
        int runs = 0;
        bool previous = false;
        for (int k = 0; k <= samples; k++)
        {
            float t = k / (float)samples;
            bool dark = IsDark(bits, start.X + ((end.X - start.X) * t), start.Y + ((end.Y - start.Y) * t));
            if (dark && !previous)
            {
                runs++;
            }

            previous = dark;
        }

        return runs;
    }

    /// <summary>
    /// Decodes the module matrix of a size: the codewords of the data regions, the Reed-Solomon correction of each interleaved block, and the data.
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <param name="size">The symbol size.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryDecode(ModuleMatrix matrix, DataMatrixEncoder.DataMatrixSize size, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = string.Empty;
        bool[] mapping = new bool[size.MappingRows * size.MappingColumns];
        for (int row = 0; row < size.Rows; row++)
        {
            for (int column = 0; column < size.Columns; column++)
            {
                if (DataMatrixEncoder.TryMapModule(size, row, column, out int index))
                {
                    mapping[index] = matrix[row, column];
                }
            }
        }

        int dataCount = size.DataCodewords;
        int total = dataCount + size.ErrorCodewords;
        int[] positions = new int[total * 8];
        DataMatrixEncoder.CodewordPositions(size, positions);
        int[] codewords = new int[total];
        for (int i = 0; i < total; i++)
        {
            int codeword = 0;
            for (int b = 0; b < 8; b++)
            {
                codeword = (codeword << 1) | (mapping[positions[(i * 8) + b]] ? 1 : 0);
            }

            codewords[i] = codeword;
        }

        // Codeword i of the data belongs to block i mod b, and check codeword j of block k is at j x b plus the column of the block, which the 144 x 144
        // symbol rotates by its long blocks.
        int blocks = size.Blocks;
        int perBlock = size.ErrorCodewords / blocks;
        int shortLength = dataCount / blocks;
        int longBlocks = dataCount % blocks;
        BinaryReedSolomonField field = new(DataMatrixEncoder.Field);
        int[] data = new int[dataCount];
        for (int block = 0; block < blocks; block++)
        {
            int count = block < longBlocks ? shortLength + 1 : shortLength;
            int column = (block + blocks - longBlocks) % blocks;
            int[] word = new int[count + perBlock];
            for (int i = 0; i < count; i++)
            {
                word[i] = codewords[block + (i * blocks)];
            }

            for (int j = 0; j < perBlock; j++)
            {
                word[count + j] = codewords[dataCount + (j * blocks) + column];
            }

            if (ReedSolomonDecoder.Correct(field, word, perBlock, 1) < 0)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                data[block + (i * blocks)] = word[i];
            }
        }

        return DataMatrixBitstream.TryDecode(data, out text, out identifier);
    }

    /// <summary>
    /// Returns a step of a length from one point towards another.
    /// </summary>
    /// <param name="from">The point.</param>
    /// <param name="to">The point to go towards.</param>
    /// <param name="length">The length of the step.</param>
    /// <returns>The step.</returns>
    private static PointF Towards(PointF from, PointF to, float length)
    {
        float distance = Length(from, to);
        return distance == 0 ? default : new PointF((to.X - from.X) * length / distance, (to.Y - from.Y) * length / distance);
    }

    /// <summary>
    /// Returns the distance between two points.
    /// </summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>The distance.</returns>
    private static float Length(PointF a, PointF b) => MathF.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>
    /// Returns whether the pixel under a point is dark. A point outside the image is light.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns><see langword="true"/> for a dark pixel.</returns>
    private static bool IsDark(BitImage bits, float x, float y)
    {
        int px = (int)MathF.Floor(x);
        int py = (int)MathF.Floor(y);
        return px >= 0 && py >= 0 && px < bits.Width && py < bits.Height && bits[px, py];
    }
}
