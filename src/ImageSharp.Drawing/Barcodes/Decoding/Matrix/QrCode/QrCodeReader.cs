// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads QR Code, ISO/IEC 18004. Three finder patterns that form the right angle of a symbol give its upper left, upper right and lower left corners. Their
/// distance in modules gives the version, and from version 2 the alignment pattern nearest the lower right corner fixes the fourth point of the perspective
/// transform that maps the module grid onto the image. From version 7 the version information sets the module count, and the image samples again when it
/// differs from the estimate.
/// </summary>
internal sealed class QrCodeReader : MatrixReader
{
    /// <summary>
    /// The most finder patterns whose triples the reader tries, the patterns that the most rows confirmed.
    /// </summary>
    private const int MaximumCandidates = 16;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.QrCode;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        QrFinderState state = context.State<QrFinderState>();
        List<FinderPattern> candidates = [];
        foreach (FinderPattern pattern in state.Patterns(context))
        {
            if (pattern.Count >= 2)
            {
                candidates.Add(pattern);
            }
        }

        if (candidates.Count < 3)
        {
            return;
        }

        candidates.Sort((a, b) => b.Count.CompareTo(a.Count));
        if (candidates.Count > MaximumCandidates)
        {
            candidates.RemoveRange(MaximumCandidates, candidates.Count - MaximumCandidates);
        }

        // The triples in the order of how close they come to an isosceles right triangle, a^2 + b^2 = c^2 with a = b.
        List<(float Distortion, FinderPattern A, FinderPattern B, FinderPattern C)> triples = [];
        for (int i = 0; i < candidates.Count - 2; i++)
        {
            for (int j = i + 1; j < candidates.Count - 1; j++)
            {
                for (int k = j + 1; k < candidates.Count; k++)
                {
                    FinderPattern a = candidates[i];
                    FinderPattern b = candidates[j];
                    FinderPattern c = candidates[k];
                    float smallest = MathF.Min(a.ModuleSize, MathF.Min(b.ModuleSize, c.ModuleSize));
                    float largest = MathF.Max(a.ModuleSize, MathF.Max(b.ModuleSize, c.ModuleSize));
                    if (largest > smallest * 1.4F)
                    {
                        continue;
                    }

                    Span<float> sides = [FinderPattern.SquaredDistance(a, b), FinderPattern.SquaredDistance(b, c), FinderPattern.SquaredDistance(a, c)];
                    sides.Sort();
                    float distortion = (MathF.Abs(sides[2] - (2 * sides[1])) + MathF.Abs(sides[2] - (2 * sides[0]))) / sides[2];
                    if (distortion < 0.5F)
                    {
                        triples.Add((distortion, a, b, c));
                    }
                }
            }
        }

        triples.Sort((x, y) => x.Distortion.CompareTo(y.Distortion));
        foreach ((float Distortion, FinderPattern A, FinderPattern B, FinderPattern C) triple in triples)
        {
            if (state.IsUsed(triple.A) || state.IsUsed(triple.B) || state.IsUsed(triple.C))
            {
                continue;
            }

            Order(triple.A, triple.B, triple.C, out FinderPattern bottomLeft, out FinderPattern topLeft, out FinderPattern topRight);
            if (TryRead(context, bottomLeft, topLeft, topRight))
            {
                state.Use(triple.A);
                state.Use(triple.B);
                state.Use(triple.C);
            }
        }
    }

    /// <summary>
    /// Orders three finder patterns: the one opposite the longest side is the upper left, and the sign of the cross product tells the lower left from the upper
    /// right.
    /// </summary>
    /// <param name="a">The first pattern.</param>
    /// <param name="b">The second pattern.</param>
    /// <param name="c">The third pattern.</param>
    /// <param name="bottomLeft">The lower left pattern.</param>
    /// <param name="topLeft">The upper left pattern.</param>
    /// <param name="topRight">The upper right pattern.</param>
    private static void Order(
        FinderPattern a,
        FinderPattern b,
        FinderPattern c,
        out FinderPattern bottomLeft,
        out FinderPattern topLeft,
        out FinderPattern topRight)
    {
        float ab = FinderPattern.Distance(a, b);
        float bc = FinderPattern.Distance(b, c);
        float ac = FinderPattern.Distance(a, c);
        if (bc >= ab && bc >= ac)
        {
            (topLeft, bottomLeft, topRight) = (a, b, c);
        }
        else if (ac >= bc && ac >= ab)
        {
            (topLeft, bottomLeft, topRight) = (b, a, c);
        }
        else
        {
            (topLeft, bottomLeft, topRight) = (c, a, b);
        }

        // In image coordinates, with y down, the upper right lies clockwise of the lower left about the upper left.
        float cross = ((topRight.X - topLeft.X) * (bottomLeft.Y - topLeft.Y)) - ((topRight.Y - topLeft.Y) * (bottomLeft.X - topLeft.X));
        if (cross < 0)
        {
            (bottomLeft, topRight) = (topRight, bottomLeft);
        }
    }

    /// <summary>
    /// Maps, samples and decodes the symbol of three finder patterns.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bottomLeft">The lower left pattern.</param>
    /// <param name="topLeft">The upper left pattern.</param>
    /// <param name="topRight">The upper right pattern.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryRead(DecodeContext context, FinderPattern bottomLeft, FinderPattern topLeft, FinderPattern topRight)
    {
        BitImage bits = context.Bits;
        float module = (ModuleSizeOneWay(bits, topLeft, topRight) + ModuleSizeOneWay(bits, topLeft, bottomLeft)) / 2F;
        if (float.IsNaN(module) || module < 1F)
        {
            return false;
        }

        // The finder pattern centres are 7 modules in from the edges, and a side is 4 times the version plus 17 modules.
        int across = (int)MathF.Round(FinderPattern.Distance(topLeft, topRight) / module);
        int down = (int)MathF.Round(FinderPattern.Distance(topLeft, bottomLeft) / module);
        int estimate = ((across + down) / 2) + 7;
        Span<int> dimensions = stackalloc int[2];
        int dimensionCount = (estimate & 3) switch
        {
            0 => Fill(dimensions, estimate + 1),
            1 => Fill(dimensions, estimate),
            2 => Fill(dimensions, estimate - 1),
            _ => Fill(dimensions, estimate - 2, estimate + 2),
        };

        for (int d = 0; d < dimensionCount; d++)
        {
            int dimension = dimensions[d];
            for (int attempt = 0; attempt < 2 && dimension is >= 21 and <= 177; attempt++)
            {
                PerspectiveTransform transform = CreateTransform(bits, topLeft, topRight, bottomLeft, module, dimension);
                if (!ModuleMatrix.TrySample(bits, transform, dimension, dimension, out ModuleMatrix matrix))
                {
                    break;
                }

                int version = QrSymbolDecoder.ReadVersion(matrix);
                if (version < 1)
                {
                    break;
                }

                // The version information names the module count; the estimate from the finder patterns can miss it in a large symbol.
                if ((4 * version) + 17 != dimension)
                {
                    dimension = (4 * version) + 17;
                    continue;
                }

                if (!QrEncoder.TryGetSize(QrFormat.Full, version, out QrEncoder.SymbolSize size))
                {
                    break;
                }

                if (QrSymbolDecoder.TryDecode(matrix, size, out string text, out string identifier)
                    || QrSymbolDecoder.TryDecode(matrix.Transpose(), size, out text, out identifier))
                {
                    PointF[] corners =
                    [
                        transform.Transform(0, 0),
                        transform.Transform(dimension, 0),
                        transform.Transform(dimension, dimension),
                        transform.Transform(0, dimension),
                    ];

                    context.Add(BarcodeFamily.QrCode, text, identifier, corners);
                    return true;
                }

                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes one or two values to a span.
    /// </summary>
    /// <param name="span">The span.</param>
    /// <param name="first">The first value.</param>
    /// <param name="second">The second value, or 0 for none.</param>
    /// <returns>The number of values.</returns>
    private static int Fill(Span<int> span, int first, int second = 0)
    {
        span[0] = first;
        span[1] = second;
        return second == 0 ? 1 : 2;
    }

    /// <summary>
    /// Creates the transform from module coordinates to the image: the finder pattern centres at 3.5 modules from their corners, and the alignment pattern
    /// nearest the lower right corner, or the corner the other three points make a parallelogram with.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="topLeft">The upper left pattern.</param>
    /// <param name="topRight">The upper right pattern.</param>
    /// <param name="bottomLeft">The lower left pattern.</param>
    /// <param name="module">The module size in pixels.</param>
    /// <param name="dimension">The modules on a side.</param>
    /// <returns>The transform.</returns>
    private static PerspectiveTransform CreateTransform(
        BitImage bits,
        FinderPattern topLeft,
        FinderPattern topRight,
        FinderPattern bottomLeft,
        float module,
        int dimension)
    {
        float far = dimension - 3.5F;
        float bottomRightX = topRight.X - topLeft.X + bottomLeft.X;
        float bottomRightY = topRight.Y - topLeft.Y + bottomLeft.Y;
        float source = far;
        if (dimension > 21)
        {
            // Annex E: the alignment pattern nearest the lower right corner has its centre 6.5 modules in from both edges, 3 modules inside the corner the
            // finder pattern centres make.
            float correction = 1F - (3F / (dimension - 7));
            float estimateX = topLeft.X + (correction * (bottomRightX - topLeft.X));
            float estimateY = topLeft.Y + (correction * (bottomRightY - topLeft.Y));
            for (int allowance = 4; allowance <= 16; allowance <<= 1)
            {
                FinderPattern? alignment = AlignmentPatternFinder.Find(bits, module, estimateX, estimateY, allowance);
                if (alignment is not null)
                {
                    bottomRightX = alignment.X;
                    bottomRightY = alignment.Y;
                    source = far - 3F;
                    break;
                }
            }
        }

        ReadOnlySpan<PointF> from = [new(3.5F, 3.5F), new(far, 3.5F), new(source, source), new(3.5F, far)];
        ReadOnlySpan<PointF> to = [topLeft.Centre, topRight.Centre, new(bottomRightX, bottomRightY), bottomLeft.Centre];
        return PerspectiveTransform.QuadrilateralToQuadrilateral(from, to);
    }

    /// <summary>
    /// Returns the module size along the line from one finder pattern centre to another: the dark, light and dark runs from each centre outwards, both ways,
    /// make 7 modules each.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="other">The other pattern.</param>
    /// <returns>The module size, or NaN.</returns>
    private static float ModuleSizeOneWay(BitImage bits, FinderPattern pattern, FinderPattern other)
    {
        float first = RunBothWays(bits, (int)pattern.X, (int)pattern.Y, (int)other.X, (int)other.Y);
        float second = RunBothWays(bits, (int)other.X, (int)other.Y, (int)pattern.X, (int)pattern.Y);
        if (float.IsNaN(first))
        {
            return second / 7F;
        }

        return float.IsNaN(second) ? first / 7F : (first + second) / 14F;
    }

    /// <summary>
    /// Returns the length of the dark, light and dark runs from a point towards another point and away from it, the width of the finder pattern along the line.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="fromX">The x coordinate of the point.</param>
    /// <param name="fromY">The y coordinate of the point.</param>
    /// <param name="toX">The x coordinate of the other point.</param>
    /// <param name="toY">The y coordinate of the other point.</param>
    /// <returns>The length in pixels, or NaN.</returns>
    private static float RunBothWays(BitImage bits, int fromX, int fromY, int toX, int toY)
    {
        float result = Run(bits, fromX, fromY, toX, toY);

        // The opposite direction, cut at the edge of the image.
        float scale = 1F;
        int otherX = fromX - (toX - fromX);
        if (otherX < 0)
        {
            scale = fromX / (float)(fromX - otherX);
            otherX = 0;
        }
        else if (otherX >= bits.Width)
        {
            scale = (bits.Width - 1 - fromX) / (float)(otherX - fromX);
            otherX = bits.Width - 1;
        }

        int otherY = (int)(fromY - ((toY - fromY) * scale));
        scale = 1F;
        if (otherY < 0)
        {
            scale = fromY / (float)(fromY - otherY);
            otherY = 0;
        }
        else if (otherY >= bits.Height)
        {
            scale = (bits.Height - 1 - fromY) / (float)(otherY - fromY);
            otherY = bits.Height - 1;
        }

        otherX = (int)(fromX + ((otherX - fromX) * scale));
        result += Run(bits, fromX, fromY, otherX, otherY);

        // The centre pixel counts in both directions.
        return result - 1F;
    }

    /// <summary>
    /// Returns the distance from a point to the end of the dark, light and dark runs along the line to another point, by Bresenham's algorithm.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="fromX">The x coordinate of the point.</param>
    /// <param name="fromY">The y coordinate of the point.</param>
    /// <param name="toX">The x coordinate of the other point.</param>
    /// <param name="toY">The y coordinate of the other point.</param>
    /// <returns>The distance in pixels, or NaN.</returns>
    private static float Run(BitImage bits, int fromX, int fromY, int toX, int toY)
    {
        bool steep = Math.Abs(toY - fromY) > Math.Abs(toX - fromX);
        if (steep)
        {
            (fromX, fromY) = (fromY, fromX);
            (toX, toY) = (toY, toX);
        }

        int dx = Math.Abs(toX - fromX);
        int dy = Math.Abs(toY - fromY);
        int error = -dx / 2;
        int stepX = fromX < toX ? 1 : -1;
        int stepY = fromY < toY ? 1 : -1;

        // Dark in states 0 and 2, light in state 1.
        int state = 0;
        int limit = toX + stepX;
        for (int x = fromX, y = fromY; x != limit; x += stepX)
        {
            int realX = steep ? y : x;
            int realY = steep ? x : y;
            if ((state == 1) == bits[realX, realY])
            {
                if (state == 2)
                {
                    return Distance(x, y, fromX, fromY);
                }

                state++;
            }

            error += dy;
            if (error > 0)
            {
                if (y == toY)
                {
                    break;
                }

                y += stepY;
                error -= dx;
            }
        }

        // The pixel past the end of the line counts as light.
        return state == 2 ? Distance(toX + stepX, toY, fromX, fromY) : float.NaN;
    }

    /// <summary>
    /// Returns the distance between two points.
    /// </summary>
    /// <param name="x1">The x coordinate of the first point.</param>
    /// <param name="y1">The y coordinate of the first point.</param>
    /// <param name="x2">The x coordinate of the second point.</param>
    /// <param name="y2">The y coordinate of the second point.</param>
    /// <returns>The distance.</returns>
    private static float Distance(int x1, int y1, int x2, int y2)
    {
        float dx = x1 - x2;
        float dy = y1 - y2;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }
}
