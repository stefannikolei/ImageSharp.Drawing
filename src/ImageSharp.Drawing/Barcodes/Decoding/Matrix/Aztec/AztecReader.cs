// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Aztec Code and Aztec Runes, ISO/IEC 24778. The bull's eye at the centre is a set of square rings of one module that a row through its centre crosses
/// in equal runs. The rings from the centre outwards give the corners of the bull's eye and tell a compact symbol, five rings, from a full-range one, seven.
/// The mode message ring around the bull's eye holds the orientation patterns at its corners and the layer and codeword counts, which the Reed-Solomon code
/// over GF(16) corrects. The perspective transform from the corners samples the symbol, the placement of <see cref="AztecEncoder.DataPositions"/> reads the
/// codewords, and <see cref="AztecBitstream"/> decodes them.
/// </summary>
internal sealed class AztecReader : MatrixReader
{
    /// <summary>
    /// The orientation bits of the four corners of the mode message ring, three per corner, in each of the four turns of the symbol.
    /// </summary>
    private static readonly int[] ExpectedCornerBits = [0xEE0, 0x1DC, 0x83B, 0x707];

    /// <summary>
    /// The field of the mode message: GF(16) with the primitive polynomial x^4 + x + 1.
    /// </summary>
    private static readonly GaloisField ModeField = new(16, 19);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Aztec | BarcodeFamily.AztecRune;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        BitImage bits = context.Bits;
        foreach (FinderPattern centre in FindCentres(bits, context.Options.TryHarder ? 1 : Math.Max(1, bits.Height / 256)))
        {
            for (int mirror = 0; mirror < 2; mirror++)
            {
                if (TryRead(context, bits, centre, mirror == 1))
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Finds the centres of bull's eyes: a dark run between six runs of its width, light first, along a row, which the column through it confirms.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="step">The rows between two scanned rows.</param>
    /// <returns>The centres, with the module size.</returns>
    private static List<FinderPattern> FindCentres(BitImage bits, int step)
    {
        List<FinderPattern> centres = [];
        int[] runs = new int[bits.Width + 2];
        int[] starts = new int[bits.Width + 2];
        for (int y = 0; y < bits.Height; y += step)
        {
            int count = LinearScanner.ToRuns(bits.Row(y), runs, starts);

            // The dark runs are at odd indices.
            for (int i = 3; i + 3 < count; i += 2)
            {
                ReadOnlySpan<int> window = runs.AsSpan(i - 3, 7);
                if (!IsEqual(window))
                {
                    continue;
                }

                float x = starts[i] + (runs[i] / 2F);
                float module = LinearReader.Sum(window, 0, 7) / 7F;
                float centreY = CrossCheckVertical(bits, (int)x, y, module);
                if (float.IsNaN(centreY))
                {
                    continue;
                }

                bool merged = false;
                foreach (FinderPattern centre in centres)
                {
                    if (centre.AboutEquals(module, x, centreY))
                    {
                        centre.Combine(x, centreY, module);
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    centres.Add(new FinderPattern(x, centreY, module));
                }
            }
        }

        return centres;
    }

    /// <summary>
    /// Returns whether runs are all about as long as their mean.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <returns><see langword="true"/> for equal runs.</returns>
    private static bool IsEqual(ReadOnlySpan<int> runs)
    {
        float module = LinearReader.Sum(runs, 0, runs.Length) / (float)runs.Length;
        foreach (int run in runs)
        {
            if (MathF.Abs(run - module) > module * 0.5F)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the seven runs about a centre along its column and returns the centre of the middle run.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row inside the dark centre.</param>
    /// <param name="module">The module size from the row.</param>
    /// <returns>The centre, or NaN.</returns>
    private static float CrossCheckVertical(BitImage bits, int x, int y, float module)
    {
        if (!bits[x, y])
        {
            return float.NaN;
        }

        int top = y;
        while (top > 0 && bits[x, top - 1])
        {
            top--;
        }

        int bottom = y;
        while (bottom < bits.Height - 1 && bits[x, bottom + 1])
        {
            bottom++;
        }

        // Three rings on each side, each a run of about one module.
        Span<int> runs = stackalloc int[7];
        runs[3] = bottom - top + 1;
        int position = top - 1;
        for (int k = 2; k >= 0; k--)
        {
            bool dark = k == 1;
            int length = 0;
            while (position >= 0 && bits[x, position] == dark && length <= module * 2)
            {
                length++;
                position--;
            }

            runs[k] = length;
        }

        position = bottom + 1;
        for (int k = 4; k < 7; k++)
        {
            bool dark = k == 5;
            int length = 0;
            while (position < bits.Height && bits[x, position] == dark && length <= module * 2)
            {
                length++;
                position++;
            }

            runs[k] = length;
        }

        float total = LinearReader.Sum(runs, 0, 7);
        return IsEqual(runs) && MathF.Abs(total - (module * 7)) < module * 2.5F ? (top + bottom + 1) / 2F : float.NaN;
    }

    /// <summary>
    /// Reads the symbol of a bull's eye.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image.</param>
    /// <param name="centre">The centre of the bull's eye.</param>
    /// <param name="mirror">Whether to read the symbol mirrored.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryRead(DecodeContext context, BitImage bits, FinderPattern centre, bool mirror)
    {
        if (!TryFindBullsEye(bits, centre, out PointF[] corners, out int rings))
        {
            return false;
        }

        if (mirror)
        {
            (corners[0], corners[2]) = (corners[2], corners[0]);
        }

        // The sides of the mode message ring from each corner, clockwise: the right side from the upper right corner as the image shows it, then the bottom,
        // the left and the top.
        bool compact = rings == 5;
        int length = 2 * rings;
        Span<int> sides = stackalloc int[4];
        for (int i = 0; i < 4; i++)
        {
            sides[i] = SampleLine(bits, corners[i], corners[(i + 1) % 4], length);
        }

        // Three orientation bits at each corner: the two at the start of a side and the one at the end of the side before it.
        int cornerBits = 0;
        foreach (int side in sides)
        {
            cornerBits = (cornerBits << 3) + ((side >> (length - 2)) << 1) + (side & 1);
        }

        cornerBits = ((cornerBits & 1) << 11) + (cornerBits >> 1);
        int shift = -1;
        for (int s = 0; s < 4; s++)
        {
            if (BitOperations.PopCount((uint)(cornerBits ^ ExpectedCornerBits[s])) <= 2)
            {
                shift = s;
                break;
            }
        }

        if (shift < 0)
        {
            return false;
        }

        // The mode message: 7 bits a side in a compact symbol, and 10 a side, either side of the grid line, in a full-range one.
        long parameters = 0;
        for (int i = 0; i < 4; i++)
        {
            int side = sides[(shift + i) % 4];
            parameters = compact
                ? (parameters << 7) + ((side >> 1) & 0x7F)
                : (parameters << 10) + ((side >> 2) & (0x1F << 5)) + ((side >> 1) & 0x1F);
        }

        PointF topLeft = corners[shift % 4];
        PointF topRight = corners[(shift + 1) % 4];
        PointF bottomRight = corners[(shift + 2) % 4];
        PointF bottomLeft = corners[(shift + 3) % 4];
        if (context.Wants(BarcodeFamily.Aztec)
            && TryCorrectParameters(parameters, compact, false, out int data)
            && TryReadData(
                bits,
                topLeft,
                topRight,
                bottomRight,
                bottomLeft,
                compact,
                rings,
                data,
                out string text,
                out string identifier,
                out PointF[] symbolCorners))
        {
            context.Add(BarcodeFamily.Aztec, text, identifier, symbolCorners);
            return true;
        }

        // Section 9: an Aztec Rune is a compact core whose mode message carries a value of 0 to 255, every 4-bit word with alternate bits inverted.
        if (compact && context.Wants(BarcodeFamily.AztecRune) && TryCorrectParameters(parameters, true, true, out int rune) && rune <= 255)
        {
            int dimension = AztecEncoder.Side(true, 0);
            PerspectiveTransform transform = CreateTransform(topLeft, topRight, bottomRight, bottomLeft, dimension, rings);
            context.Add(BarcodeFamily.AztecRune, rune.ToString("D3", CultureInfo.InvariantCulture), "]zC", Corners(transform, dimension));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Samples and decodes the data layers that the mode message counts.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="topLeft">The upper left corner of the mode message ring.</param>
    /// <param name="topRight">The upper right corner.</param>
    /// <param name="bottomRight">The lower right corner.</param>
    /// <param name="bottomLeft">The lower left corner.</param>
    /// <param name="compact">Whether the symbol is compact.</param>
    /// <param name="rings">The rings of the bull's eye.</param>
    /// <param name="data">The data words of the mode message.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <param name="corners">The corners of the symbol.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryReadData(
        BitImage bits,
        PointF topLeft,
        PointF topRight,
        PointF bottomRight,
        PointF bottomLeft,
        bool compact,
        int rings,
        int data,
        out string text,
        out string identifier,
        out PointF[] corners)
    {
        text = string.Empty;
        identifier = string.Empty;
        corners = [];

        // Section 7.2.3: the layer count less one and the data codeword count less one, in 2 and 6 bits compact or 5 and 11 bits full-range.
        int layers = compact ? (data >> 6) + 1 : (data >> 11) + 1;
        int dataCodewords = compact ? (data & 0x3F) + 1 : (data & 0x7FF) + 1;
        if (layers > (compact ? 4 : 32))
        {
            return false;
        }

        int side = AztecEncoder.Side(compact, layers);
        PerspectiveTransform transform = CreateTransform(topLeft, topRight, bottomRight, bottomLeft, side, rings);
        if (!compact)
        {
            transform = Refine(bits, transform, side);
        }

        if (!ModuleMatrix.TrySample(bits, transform, side, side, out ModuleMatrix matrix)
            || !TryDecode(matrix, compact, layers, dataCodewords, out text, out identifier))
        {
            return false;
        }

        corners = Corners(transform, side);
        return true;
    }

    /// <summary>
    /// Follows the rings of a bull's eye outwards from its centre along the four diagonals, while each ring is a square of one colour about as wide as the
    /// rings so far predict, and returns the centres of the corner modules of the mode message ring.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="centre">The centre.</param>
    /// <param name="corners">The corners, upper right, lower right, lower left and upper left as the image shows them.</param>
    /// <param name="rings">The rings, 5 for a compact symbol and 7 for a full-range one.</param>
    /// <returns><see langword="true"/> for a bull's eye of five or seven rings.</returns>
    private static bool TryFindBullsEye(BitImage bits, FinderPattern centre, out PointF[] corners, out int rings)
    {
        corners = [];
        (int X, int Y) start = ((int)centre.X, (int)centre.Y);
        (int X, int Y) a = start;
        (int X, int Y) b = start;
        (int X, int Y) c = start;
        (int X, int Y) d = start;
        bool colour = true;
        for (rings = 1; rings < 9; rings++)
        {
            (int X, int Y) outA = FirstDifferent(bits, a, colour, 1, -1);
            (int X, int Y) outB = FirstDifferent(bits, b, colour, 1, 1);
            (int X, int Y) outC = FirstDifferent(bits, c, colour, -1, 1);
            (int X, int Y) outD = FirstDifferent(bits, d, colour, -1, -1);
            if (rings > 2)
            {
                // The ring is as much wider than the one inside it as one more ring of a module on each side makes it.
                float ratio = Distance(outD, outA) * rings / (Distance(d, a) * (rings + 2));
                if (ratio < 0.75F || ratio > 1.25F || !IsRingOfOneColour(bits, outA, outB, outC, outD, centre.ModuleSize))
                {
                    break;
                }
            }

            (a, b, c, d) = (outA, outB, outC, outD);
            colour = !colour;
        }

        if (rings is not (5 or 7))
        {
            return false;
        }

        // The corners of the last ring moved half a pixel outwards, then out to the centres of the modules just outside the bull's eye: the ring of 2 rings - 3
        // modules grows to 2 rings.
        PointF[] edge =
        [
            new(a.X + 0.5F, a.Y - 0.5F),
            new(b.X + 0.5F, b.Y + 0.5F),
            new(c.X - 0.5F, c.Y + 0.5F),
            new(d.X - 0.5F, d.Y - 0.5F),
        ];

        float scale = (2 * rings) / (2F * ((2 * rings) - 3));
        corners = new PointF[4];
        for (int i = 0; i < 2; i++)
        {
            PointF p = edge[i];
            PointF q = edge[i + 2];
            float centreX = (p.X + q.X) / 2;
            float centreY = (p.Y + q.Y) / 2;
            corners[i] = new PointF(centreX + (scale * (p.X - q.X)), centreY + (scale * (p.Y - q.Y)));
            corners[i + 2] = new PointF(centreX - (scale * (p.X - q.X)), centreY - (scale * (p.Y - q.Y)));
        }

        foreach (PointF corner in corners)
        {
            if (corner.X < 0 || corner.Y < 0 || corner.X >= bits.Width || corner.Y >= bits.Height)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Moves from a point along a diagonal while the pixels have a colour, then along the row and the column to the edge of that colour.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="start">The point.</param>
    /// <param name="colour">The colour, <see langword="true"/> for dark.</param>
    /// <param name="dx">The x step.</param>
    /// <param name="dy">The y step.</param>
    /// <returns>The last point of the colour.</returns>
    private static (int X, int Y) FirstDifferent(BitImage bits, (int X, int Y) start, bool colour, int dx, int dy)
    {
        int x = start.X + dx;
        int y = start.Y + dy;
        while (IsInside(bits, x, y) && bits[x, y] == colour)
        {
            x += dx;
            y += dy;
        }

        x -= dx;
        y -= dy;
        while (IsInside(bits, x, y) && bits[x, y] == colour)
        {
            x += dx;
        }

        x -= dx;
        while (IsInside(bits, x, y) && bits[x, y] == colour)
        {
            y += dy;
        }

        y -= dy;
        return (x, y);
    }

    /// <summary>
    /// Returns whether the square half a module inside four corners is of one colour, the colour the ring should have.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="a">The upper right corner.</param>
    /// <param name="b">The lower right corner.</param>
    /// <param name="c">The lower left corner.</param>
    /// <param name="d">The upper left corner.</param>
    /// <param name="module">The module size in pixels.</param>
    /// <returns><see langword="true"/> for a square of one colour.</returns>
    private static bool IsRingOfOneColour(BitImage bits, (int X, int Y) a, (int X, int Y) b, (int X, int Y) c, (int X, int Y) d, float module)
    {
        int inset = Math.Max(1, (int)(module / 2));
        (int X, int Y) p1 = (Math.Max(0, a.X - inset), Math.Min(bits.Height - 1, a.Y + inset));
        (int X, int Y) p2 = (Math.Max(0, b.X - inset), Math.Max(0, b.Y - inset));
        (int X, int Y) p3 = (Math.Min(bits.Width - 1, c.X + inset), Math.Max(0, Math.Min(bits.Height - 1, c.Y - inset)));
        (int X, int Y) p4 = (Math.Min(bits.Width - 1, d.X + inset), Math.Min(bits.Height - 1, d.Y + inset));
        int colour = Colour(bits, p4, p1);
        return colour != 0 && Colour(bits, p1, p2) == colour && Colour(bits, p2, p3) == colour && Colour(bits, p3, p4) == colour;
    }

    /// <summary>
    /// Returns the colour along a line: 1 when nine tenths of it have the colour of its first pixel and that is dark, -1 when they have it and it is light, and
    /// 0 when the line is mixed.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    /// <returns>The colour.</returns>
    private static int Colour(BitImage bits, (int X, int Y) from, (int X, int Y) to)
    {
        float distance = Distance(from, to);
        if (distance == 0)
        {
            return 0;
        }

        float dx = (to.X - from.X) / distance;
        float dy = (to.Y - from.Y) / distance;
        bool model = bits[from.X, from.Y];
        int errors = 0;
        int steps = (int)MathF.Floor(distance);
        float x = from.X;
        float y = from.Y;
        for (int i = 0; i < steps; i++)
        {
            int px = Math.Clamp((int)MathF.Round(x), 0, bits.Width - 1);
            int py = Math.Clamp((int)MathF.Round(y), 0, bits.Height - 1);
            if (bits[px, py] != model)
            {
                errors++;
            }

            x += dx;
            y += dy;
        }

        float share = errors / distance;
        if (share > 0.1F && share < 0.9F)
        {
            return 0;
        }

        return (share <= 0.1F) == model ? 1 : -1;
    }

    /// <summary>
    /// Samples modules along a line from one corner towards the next, the first one at the corner.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="from">The first corner.</param>
    /// <param name="to">The next corner.</param>
    /// <param name="count">The number of modules.</param>
    /// <returns>The modules, the first in the highest bit.</returns>
    private static int SampleLine(BitImage bits, PointF from, PointF to, int count)
    {
        int result = 0;
        float dx = (to.X - from.X) / count;
        float dy = (to.Y - from.Y) / count;
        for (int i = 0; i < count; i++)
        {
            int x = Math.Clamp((int)MathF.Round(from.X + (i * dx)), 0, bits.Width - 1);
            int y = Math.Clamp((int)MathF.Round(from.Y + (i * dy)), 0, bits.Height - 1);
            if (bits[x, y])
            {
                result |= 1 << (count - i - 1);
            }
        }

        return result;
    }

    /// <summary>
    /// Corrects the mode message, section 7.2: 2 or 4 data words of 4 bits and 5 or 6 check words over GF(16).
    /// </summary>
    /// <param name="parameters">The 28 or 40 bits of the mode message.</param>
    /// <param name="compact">Whether the symbol is compact.</param>
    /// <param name="rune">Whether the words have alternate bits inverted, as in a rune.</param>
    /// <param name="data">The data words as a number.</param>
    /// <returns><see langword="true"/> when the message corrects.</returns>
    private static bool TryCorrectParameters(long parameters, bool compact, bool rune, out int data)
    {
        int count = compact ? 7 : 10;
        int dataWords = compact ? 2 : 4;
        Span<int> words = stackalloc int[count];
        for (int i = count - 1; i >= 0; i--)
        {
            words[i] = (int)(parameters & 0xF) ^ (rune ? 0b1010 : 0);
            parameters >>= 4;
        }

        data = 0;
        if (ReedSolomonDecoder.Correct(new BinaryReedSolomonField(ModeField), words, count - dataWords, 1) < 0)
        {
            return false;
        }

        for (int i = 0; i < dataWords; i++)
        {
            data = (data << 4) + words[i];
        }

        return true;
    }

    /// <summary>
    /// Creates the transform from module coordinates to the image, with the corners of the mode message ring at the centres of the modules a ring count from the
    /// centre of the symbol.
    /// </summary>
    /// <param name="topLeft">The upper left corner of the mode message ring.</param>
    /// <param name="topRight">The upper right corner.</param>
    /// <param name="bottomRight">The lower right corner.</param>
    /// <param name="bottomLeft">The lower left corner.</param>
    /// <param name="dimension">The modules on a side.</param>
    /// <param name="rings">The rings of the bull's eye.</param>
    /// <returns>The transform.</returns>
    private static PerspectiveTransform CreateTransform(PointF topLeft, PointF topRight, PointF bottomRight, PointF bottomLeft, int dimension, int rings)
    {
        float low = (dimension / 2F) - rings;
        float high = (dimension / 2F) + rings;
        ReadOnlySpan<PointF> from = [new(low, low), new(high, low), new(high, high), new(low, high)];
        ReadOnlySpan<PointF> to = [topLeft, topRight, bottomRight, bottomLeft];
        return PerspectiveTransform.QuadrilateralToQuadrilateral(from, to);
    }

    /// <summary>
    /// Refines the transform of a full-range symbol on its reference grid, section 7.1.4: the row and the column through the centre alternate from the core to
    /// the edges, so the edges between their modules sit where the transform must put them. The transform from the bull's eye predicts those edges 16
    /// modules further out each time, close enough to find them, and the edges found make the next transform, out to the last module edge of the symbol.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="transform">The transform from the bull's eye.</param>
    /// <param name="dimension">The modules on a side.</param>
    /// <returns>The refined transform, or the transform when an edge is not where the grid needs it.</returns>
    private static PerspectiveTransform Refine(BitImage bits, PerspectiveTransform transform, int dimension)
    {
        int half = dimension / 2;
        float middle = half + 0.5F;
        Span<PointF> from = stackalloc PointF[4];
        Span<PointF> to = stackalloc PointF[4];
        for (int reach = 16; ; reach += 16)
        {
            // The edge between the modules reach - 1 and reach from the centre, in each of the four directions.
            int r = Math.Min(reach, half);
            ReadOnlySpan<int> signs = [1, 0, 0, 1, -1, 0, 0, -1];
            for (int direction = 0; direction < 4; direction++)
            {
                int sx = signs[direction * 2];
                int sy = signs[(direction * 2) + 1];

                // The centres of the two modules either side of the edge, along the line through the centre.
                float inner = half + 0.5F + (sx + sy > 0 ? r - 1 : -(r - 1));
                float outer = half + 0.5F + (sx + sy > 0 ? r : -r);
                float edge = half + (sx + sy > 0 ? r : 1 - r);
                PointF a = sx != 0 ? transform.Transform(inner, middle) : transform.Transform(middle, inner);
                PointF b = sx != 0 ? transform.Transform(outer, middle) : transform.Transform(middle, outer);
                if (!TryFindEdge(bits, a, b, out PointF found))
                {
                    return transform;
                }

                from[direction] = sx != 0 ? new PointF(edge, middle) : new PointF(middle, edge);
                to[direction] = found;
            }

            transform = PerspectiveTransform.QuadrilateralToQuadrilateral(from, to);
            if (r == half)
            {
                return transform;
            }
        }
    }

    /// <summary>
    /// Finds the edge between two modules of different colour along the line between their centres, the change of colour nearest the middle.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="a">The centre of the inner module.</param>
    /// <param name="b">The centre of the outer module.</param>
    /// <param name="edge">The edge.</param>
    /// <returns><see langword="true"/> when the two centres differ in colour.</returns>
    private static bool TryFindEdge(BitImage bits, PointF a, PointF b, out PointF edge)
    {
        edge = default;
        const int steps = 32;
        bool first = IsDark(bits, a.X, a.Y);
        if (first == IsDark(bits, b.X, b.Y))
        {
            return false;
        }

        float best = float.MaxValue;
        bool previous = first;
        for (int k = 1; k <= steps; k++)
        {
            float t = k / (float)steps;
            bool dark = IsDark(bits, a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
            if (dark != previous)
            {
                float at = (k - 0.5F) / steps;
                if (MathF.Abs(at - 0.5F) < MathF.Abs(best - 0.5F))
                {
                    best = at;
                }
            }

            previous = dark;
        }

        edge = new PointF(a.X + ((b.X - a.X) * best), a.Y + ((b.Y - a.Y) * best));
        return true;
    }

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

    /// <summary>
    /// Returns the corners of a symbol in the image.
    /// </summary>
    /// <param name="transform">The transform from module coordinates.</param>
    /// <param name="dimension">The modules on a side.</param>
    /// <returns>The corners, from the upper left of the symbol.</returns>
    private static PointF[] Corners(PerspectiveTransform transform, int dimension)
        => [transform.Transform(0, 0), transform.Transform(dimension, 0), transform.Transform(dimension, dimension), transform.Transform(0, dimension)];

    /// <summary>
    /// Reads, corrects and decodes the codewords of a sampled symbol.
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <param name="compact">Whether the symbol is compact.</param>
    /// <param name="layers">The number of data layers.</param>
    /// <param name="dataCodewords">The number of data codewords.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryDecode(ModuleMatrix matrix, bool compact, int layers, int dataCodewords, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = string.Empty;
        int total = AztecEncoder.Codewords(compact, layers, out int size);
        if (dataCodewords >= total)
        {
            return false;
        }

        // The codewords stand at the end of the capacity of the layers.
        int capacityBits = AztecEncoder.CapacityBits(compact, layers);
        int[] positions = new int[capacityBits];
        AztecEncoder.DataPositions(compact, layers, positions);
        int offset = capacityBits - (total * size);
        int[] codewords = new int[total];
        for (int k = 0; k < total; k++)
        {
            int codeword = 0;
            for (int j = 0; j < size; j++)
            {
                codeword = (codeword << 1) | (matrix.At(positions[offset + (k * size) + j]) ? 1 : 0);
            }

            codewords[k] = codeword;
        }

        GaloisField field = size switch
        {
            6 => new GaloisField(64, 67),
            8 => new GaloisField(256, 301),
            10 => new GaloisField(1024, 1033),
            _ => new GaloisField(4096, 4201),
        };

        if (ReedSolomonDecoder.Correct(new BinaryReedSolomonField(field), codewords, total - dataCodewords, 1) < 0)
        {
            return false;
        }

        // Section 7.3.2.3: a codeword of B - 1 equal bits and a dummy bit carries the B - 1 bits only, and no codeword is all zeros or all ones.
        int ones = (1 << size) - 1;
        List<byte> bits = new(dataCodewords * size);
        for (int k = 0; k < dataCodewords; k++)
        {
            int codeword = codewords[k];
            if (codeword == 0 || codeword == ones)
            {
                return false;
            }

            if (codeword == 1 || codeword == ones - 1)
            {
                for (int j = 0; j < size - 1; j++)
                {
                    bits.Add((byte)(codeword > 1 ? 1 : 0));
                }

                continue;
            }

            for (int j = size - 1; j >= 0; j--)
            {
                bits.Add((byte)((codeword >> j) & 1));
            }
        }

        return AztecBitstream.TryDecode(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bits), out text, out identifier);
    }

    /// <summary>
    /// Returns whether a pixel is inside the image.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns><see langword="true"/> when the pixel is inside.</returns>
    private static bool IsInside(BitImage bits, int x, int y) => x >= 0 && y >= 0 && x < bits.Width && y < bits.Height;

    /// <summary>
    /// Returns the distance between two pixels.
    /// </summary>
    /// <param name="a">The first pixel.</param>
    /// <param name="b">The second pixel.</param>
    /// <returns>The distance.</returns>
    private static float Distance((int X, int Y) a, (int X, int Y) b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }
}
