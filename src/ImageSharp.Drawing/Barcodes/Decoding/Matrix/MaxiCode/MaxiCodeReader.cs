// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads MaxiCode, ISO/IEC 16023: 33 rows of 30 hexagonal modules, the odd rows half a module to the right, around a finder of three dark rings. A line
/// through the centre of the finder crosses the rings in runs of about 0.77 X with a light centre of 1.16 X, Table 8, which gives the centre and the module
/// size. The dark modules of the orientation patterns and the modules that are always light give the rotation, the modules sample at their centres, and the
/// Reed-Solomon code over GF(64) corrects the primary message and the two interleaved blocks of the secondary message.
/// </summary>
internal sealed class MaxiCodeReader : MatrixReader
{
    /// <summary>
    /// The distance between the rows in modules, Y = (1.5/√3) W, Table 7.
    /// </summary>
    private static readonly float RowPitch = (float)(1.5 / Math.Sqrt(3));

    /// <summary>
    /// The modules that are light in every symbol: outside the finder, and neither a data module nor a dark module of the orientation patterns.
    /// </summary>
    private static readonly short[] LightModules = CreateLightModules();

    /// <summary>
    /// The width in modules of the light ring inside the outer dark ring of the finder, from edge to edge through the centre: twice the radius R5 of Table 8.
    /// </summary>
    private static readonly float InnerSpan = (float)(2 * HexagonalEmitter.FinderRadii[4] / HexagonalEmitter.TableXDimension);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.MaxiCode;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        BitImage bits = context.Bits;
        foreach (FinderPattern centre in FindCentres(bits))
        {
            if (TryRead(context, bits, centre))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Finds the centres of finders: a light run between eight runs of about two thirds of its width, which the column through it confirms.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <returns>The centres, with the module size.</returns>
    private static List<FinderPattern> FindCentres(BitImage bits)
    {
        List<FinderPattern> centres = [];
        int[] runs = new int[bits.Width + 2];
        int[] starts = new int[bits.Width + 2];
        for (int y = 0; y < bits.Height; y++)
        {
            int count = LinearScanner.ToRuns(bits.Row(y), runs, starts);

            // The light runs are at even indices.
            for (int i = 4; i + 4 < count; i += 2)
            {
                if (!IsFinder(runs.AsSpan(i - 4, 9)))
                {
                    continue;
                }

                float x = starts[i] + (runs[i] / 2F);
                float ring = (LinearReader.Sum(runs, i - 4, 9) - runs[i]) / 8F;
                float centreY = CrossCheckVertical(bits, (int)x, y, ring);
                if (float.IsNaN(centreY))
                {
                    continue;
                }

                // The nine runs reach from the inner edge of the outer dark ring on one side to the other, 2 x 3.64 X, Table 8.
                float module = LinearReader.Sum(runs, i - 4, 9) / InnerSpan;
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

        centres.Sort((a, b) => b.Count.CompareTo(a.Count));
        return centres;
    }

    /// <summary>
    /// Returns whether nine runs, light first, are the inner rings of a finder: eight runs of one width about a light centre of one to two and a half times
    /// that width.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <returns><see langword="true"/> for a finder.</returns>
    private static bool IsFinder(ReadOnlySpan<int> runs)
    {
        float ring = (LinearReader.Sum(runs, 0, 9) - runs[4]) / 8F;
        if (ring < 1F)
        {
            return false;
        }

        for (int k = 0; k < 9; k++)
        {
            if (k != 4 && MathF.Abs(runs[k] - ring) > ring * 0.5F)
            {
                return false;
            }
        }

        return runs[4] >= ring && runs[4] <= ring * 2.5F;
    }

    /// <summary>
    /// Reads the nine runs about a centre along its column and returns the centre of the light run.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row inside the light centre.</param>
    /// <param name="ring">The width of a ring from the row.</param>
    /// <returns>The centre, or NaN.</returns>
    private static float CrossCheckVertical(BitImage bits, int x, int y, float ring)
    {
        if (bits[x, y])
        {
            return float.NaN;
        }

        int top = y;
        while (top > 0 && !bits[x, top - 1])
        {
            top--;
        }

        int bottom = y;
        while (bottom < bits.Height - 1 && !bits[x, bottom + 1])
        {
            bottom++;
        }

        Span<int> runs = stackalloc int[9];
        runs[4] = bottom - top + 1;
        int position = top - 1;
        for (int k = 3; k >= 0; k--)
        {
            bool dark = (k & 1) == 1;
            while (position >= 0 && bits[x, position] == dark && runs[k] <= ring * 3)
            {
                runs[k]++;
                position--;
            }
        }

        position = bottom + 1;
        for (int k = 5; k < 9; k++)
        {
            bool dark = (k & 1) == 1;
            while (position < bits.Height && bits[x, position] == dark && runs[k] <= ring * 3)
            {
                runs[k]++;
                position++;
            }
        }

        return IsFinder(runs) ? (top + bottom + 1) / 2F : float.NaN;
    }

    /// <summary>
    /// Finds the rotation and the module size about a finder, then samples and decodes the symbol.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image.</param>
    /// <param name="centre">The centre of the finder.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryRead(DecodeContext context, BitImage bits, FinderPattern centre)
    {
        float module = centre.ModuleSize;

        // The rotation in whole degrees first, then in quarters about the best with the module size within 8 %.
        int best = -1;
        float bestAngle = 0;
        for (int degree = 0; degree < 360; degree++)
        {
            int score = Score(bits, centre, module, degree * MathF.PI / 180F);
            if (score > best)
            {
                best = score;
                bestAngle = degree;
            }
        }

        float bestScale = 1F;
        float sumAngle = 0;
        float sumScale = 0;
        int ties = 0;
        best = -1;
        for (float angle = bestAngle - 1.5F; angle <= bestAngle + 1.5F; angle += 0.25F)
        {
            for (float scale = 0.92F; scale <= 1.081F; scale += 0.005F)
            {
                int score = Score(bits, centre, module * scale, angle * MathF.PI / 180F);
                if (score > best)
                {
                    best = score;
                    sumAngle = angle;
                    sumScale = scale;
                    ties = 1;
                }
                else if (score == best)
                {
                    sumAngle += angle;
                    sumScale += scale;
                    ties++;
                }
            }
        }

        bestAngle = sumAngle / ties;
        bestScale = sumScale / ties;
        float theta = bestAngle * MathF.PI / 180F;
        module *= bestScale;

        int[] codewords = new int[MaxiCodeEncoder.CodewordCount];
        ReadOnlySpan<short> map = MaxiCodeEncoder.ModuleMap;
        for (int i = 0; i < codewords.Length; i++)
        {
            int codeword = 0;
            for (int b = 0; b < 6; b++)
            {
                codeword = (codeword << 1) | (IsDark(bits, centre, module, theta, map[(i * 6) + b]) ? 1 : 0);
            }

            codewords[i] = codeword;
        }

        if (!TryCorrect(codewords, out int[] data, out int mode)
            || !MaxiCodeBitstream.TryDecode(data, mode, out string text, out string identifier))
        {
            return false;
        }

        context.Add(BarcodeFamily.MaxiCode, text, identifier, Corners(centre, module, theta));
        return true;
    }

    /// <summary>
    /// Corrects the primary message, finds the mode in its first codeword, and corrects the two blocks of the secondary message.
    /// </summary>
    /// <param name="codewords">The 144 codewords.</param>
    /// <param name="data">The data codewords: the 10 of the primary message, then those of the secondary message.</param>
    /// <param name="mode">The mode.</param>
    /// <returns><see langword="true"/> when every block corrects.</returns>
    private static bool TryCorrect(int[] codewords, out int[] data, out int mode)
    {
        data = [];
        mode = 0;
        BinaryReedSolomonField field = new(MaxiCodeEncoder.Field);
        Span<int> primary = codewords.AsSpan(0, MaxiCodeEncoder.PrimaryDataCodewords + MaxiCodeEncoder.PrimaryCheckCodewords);
        if (ReedSolomonDecoder.Correct(field, primary, MaxiCodeEncoder.PrimaryCheckCodewords, 1) < 0)
        {
            return false;
        }

        // The mode is the low four bits of the first codeword.
        mode = codewords[0] & 0xF;
        if (mode is < 2 or > 6)
        {
            return false;
        }

        // Section 4.3: mode 5 takes 68 secondary data codewords and 28 check codewords a block, the other modes 84 and 20.
        int secondaryLength = mode == 5 ? MaxiCodeEncoder.EnhancedCapacity + 1 - MaxiCodeEncoder.PrimaryDataCodewords : MaxiCodeEncoder.StandardSecondaryLength;
        int checkCount = mode == 5 ? MaxiCodeEncoder.EnhancedSecondaryCheckCodewords : MaxiCodeEncoder.StandardSecondaryCheckCodewords;
        int half = secondaryLength / 2;
        Span<int> secondary = codewords.AsSpan(MaxiCodeEncoder.SecondaryStart);
        data = new int[MaxiCodeEncoder.PrimaryDataCodewords + secondaryLength];
        primary[..MaxiCodeEncoder.PrimaryDataCodewords].CopyTo(data);
        Span<int> word = stackalloc int[half + checkCount];
        for (int block = 0; block < 2; block++)
        {
            for (int i = 0; i < half; i++)
            {
                word[i] = secondary[block + (2 * i)];
            }

            for (int i = 0; i < checkCount; i++)
            {
                word[half + i] = secondary[secondaryLength + (2 * i) + block];
            }

            if (ReedSolomonDecoder.Correct(field, word, checkCount, 1) < 0)
            {
                return false;
            }

            for (int i = 0; i < half; i++)
            {
                data[MaxiCodeEncoder.PrimaryDataCodewords + block + (2 * i)] = word[i];
            }
        }

        return true;
    }

    /// <summary>
    /// Scores a rotation and a module size: the samples of the rings of the finder that read their colour, the dark modules of the orientation patterns that
    /// read dark and the modules that are always light that read light.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="centre">The centre of the finder.</param>
    /// <param name="module">The module width in pixels.</param>
    /// <param name="theta">The rotation in radians.</param>
    /// <returns>The score.</returns>
    private static int Score(BitImage bits, FinderPattern centre, float module, float theta)
    {
        // The middle of each ring of the finder, Table 8, all round.
        int score = 0;
        ReadOnlySpan<double> radii = HexagonalEmitter.FinderRadii;
        for (int ring = 0; ring < radii.Length - 1; ring++)
        {
            float radius = (float)((radii[ring] + radii[ring + 1]) / 2 / HexagonalEmitter.TableXDimension) * module;
            bool dark = (ring & 1) == 0;
            for (int k = 0; k < 12; k++)
            {
                float angle = theta + (k * MathF.PI / 6);
                int x = (int)MathF.Floor(centre.X + (radius * MathF.Cos(angle)));
                int y = (int)MathF.Floor(centre.Y + (radius * MathF.Sin(angle)));
                bool read = x >= 0 && y >= 0 && x < bits.Width && y < bits.Height && bits[x, y];
                score += read == dark ? 1 : 0;
            }
        }

        foreach (short index in MaxiCodeEncoder.OrientationModules)
        {
            score += IsDark(bits, centre, module, theta, index) ? 1 : 0;
        }

        foreach (short index in LightModules)
        {
            score += IsDark(bits, centre, module, theta, index) ? 0 : 1;
        }

        return score;
    }

    /// <summary>
    /// Returns whether the pixel under the centre of a module is dark. A pixel outside the image is light.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="centre">The centre of the finder.</param>
    /// <param name="module">The module width in pixels.</param>
    /// <param name="theta">The rotation in radians.</param>
    /// <param name="index">The module, row x 30 + column.</param>
    /// <returns><see langword="true"/> for a dark module.</returns>
    private static bool IsDark(BitImage bits, FinderPattern centre, float module, float theta, int index)
    {
        PointF point = ModuleCentre(centre, module, theta, index / MaxiCodeEncoder.Columns, index % MaxiCodeEncoder.Columns);
        int x = (int)MathF.Floor(point.X);
        int y = (int)MathF.Floor(point.Y);
        return x >= 0 && y >= 0 && x < bits.Width && y < bits.Height && bits[x, y];
    }

    /// <summary>
    /// Returns the centre of a module in the image: the odd rows stand half a module to the right, and the rows stand Y apart, about the module the finder is
    /// centred on.
    /// </summary>
    /// <param name="centre">The centre of the finder.</param>
    /// <param name="module">The module width in pixels.</param>
    /// <param name="theta">The rotation in radians.</param>
    /// <param name="row">The row.</param>
    /// <param name="column">The column, which can be a fraction for the corners.</param>
    /// <returns>The point.</returns>
    private static PointF ModuleCentre(FinderPattern centre, float module, float theta, float row, float column)
    {
        float dx = (column - HexagonalEmitter.FinderColumn + (((int)row & 1) == 1 ? 0.5F : 0)) * module;
        float dy = (row - HexagonalEmitter.FinderRow) * RowPitch * module;
        float cos = MathF.Cos(theta);
        float sin = MathF.Sin(theta);
        return new PointF(centre.X + (dx * cos) - (dy * sin), centre.Y + (dx * sin) + (dy * cos));
    }

    /// <summary>
    /// Returns the corners of a symbol: half a module outside the centres of its corner modules.
    /// </summary>
    /// <param name="centre">The centre of the finder.</param>
    /// <param name="module">The module width in pixels.</param>
    /// <param name="theta">The rotation in radians.</param>
    /// <returns>The corners, from the upper left.</returns>
    private static PointF[] Corners(FinderPattern centre, float module, float theta)
    {
        float last = MaxiCodeEncoder.Rows - 1;
        return
        [
            ModuleCentre(centre, module, theta, -0.5F, -0.5F),
            ModuleCentre(centre, module, theta, -0.5F, MaxiCodeEncoder.Columns),
            ModuleCentre(centre, module, theta, last + 0.5F, MaxiCodeEncoder.Columns - 0.5F),
            ModuleCentre(centre, module, theta, last + 0.5F, -1F),
        ];
    }

    /// <summary>
    /// Collects the modules that are always light: outside the outer dark ring of the finder by half a module, and neither a data module nor a dark module of
    /// the orientation patterns.
    /// </summary>
    /// <returns>The modules, row x 30 + column.</returns>
    private static short[] CreateLightModules()
    {
        int count = MaxiCodeEncoder.Rows * MaxiCodeEncoder.Columns;
        bool[] taken = new bool[count];
        foreach (short index in MaxiCodeEncoder.ModuleMap)
        {
            taken[index] = true;
        }

        foreach (short index in MaxiCodeEncoder.OrientationModules)
        {
            taken[index] = true;
        }

        List<short> light = [];
        FinderPattern origin = new(0, 0, 1);
        float outer = (float)(HexagonalEmitter.FinderRadii[^1] / HexagonalEmitter.TableXDimension) + 0.6F;
        for (int index = 0; index < count; index++)
        {
            PointF point = ModuleCentre(origin, 1, 0, index / MaxiCodeEncoder.Columns, index % MaxiCodeEncoder.Columns);
            if (!taken[index] && MathF.Sqrt((point.X * point.X) + (point.Y * point.Y)) > outer)
            {
                light.Add((short)index);
            }
        }

        return [.. light];
    }
}
