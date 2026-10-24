// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Finds the rows of bars of the height modulated symbologies: the connected dark areas that are bars, taller than wide and filled, joined left to right into
/// rows of bars of one width at one pitch that share the band of the row. The top and the bottom of the row give each bar its state, which the readers turn
/// into data. A row also reads upside down, its bars in reverse and their ascenders and descenders swapped, and the columns of the image read as rows when the
/// options try harder.
/// </summary>
internal static class HeightModulatedScanner
{
    /// <summary>
    /// The part of the row height from the top or the bottom within which a bar end counts as an ascender or a descender.
    /// </summary>
    private const float ExtenderTolerance = 0.2F;

    /// <summary>
    /// Scans the image with the readers.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="readers">The readers whose families the options name.</param>
    public static void Scan(DecodeContext context, ReadOnlySpan<HeightModulatedReader> readers)
    {
        if (readers.IsEmpty)
        {
            return;
        }

        ScanImage(context, context.Bits, false, readers);
        if (context.Options.TryHarder)
        {
            ScanImage(context, context.TurnedBits, true, readers);
        }
    }

    /// <summary>
    /// Scans one orientation of the image.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image in this orientation.</param>
    /// <param name="turned">Whether the image is the quarter turn of the source.</param>
    /// <param name="readers">The readers.</param>
    private static void ScanImage(DecodeContext context, BitImage bits, bool turned, ReadOnlySpan<HeightModulatedReader> readers)
    {
        List<Component> bars = [];
        foreach (Component component in Components.Find(bits))
        {
            // A bar is taller than wide and fills most of its bounding box. A tracker is the shortest bar, about twice as tall as wide.
            if (component.Height >= 3 && component.Height * 2 >= component.Width * 3 && component.Pixels * 4 >= component.Width * component.Height * 3)
            {
                bars.Add(component);
            }
        }

        bars.Sort((a, b) => a.Left.CompareTo(b.Left));
        bool[] used = new bool[bars.Count];
        List<int> row = [];
        for (int i = 0; i < bars.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            Chain(bars, used, i, row);
            if (row.Count < 2)
            {
                continue;
            }

            foreach (int index in row)
            {
                used[index] = true;
            }

            ReadRow(context, bars, row, bits, turned, readers);
        }
    }

    /// <summary>
    /// Joins bars into a row from a first bar: each next bar is the nearest to the right that has the width of the row, stands in its band and keeps its
    /// pitch.
    /// </summary>
    /// <param name="bars">The bars, by their left edge.</param>
    /// <param name="used">The bars that are in a row already.</param>
    /// <param name="first">The first bar.</param>
    /// <param name="row">The bars of the row, as indices.</param>
    private static void Chain(List<Component> bars, bool[] used, int first, List<int> row)
    {
        row.Clear();
        row.Add(first);
        Component start = bars[first];
        int top = start.Top;
        int bottom = start.Bottom;
        float width = start.Width;
        float pitch = 0;
        int current = first;
        while (true)
        {
            Component last = bars[current];
            int next = -1;
            for (int j = current + 1; j < bars.Count; j++)
            {
                Component candidate = bars[j];
                float distance = candidate.CenterX - last.CenterX;
                if (candidate.Left > last.Right + (pitch > 0 ? pitch * 2 : width * 6) + 1)
                {
                    break;
                }

                if (used[j] || candidate.Left <= last.Right || candidate.Width > (width * 2) + 1 || (candidate.Width * 2) + 1 < width)
                {
                    continue;
                }

                // The bar stands in the band of the row: it overlaps the band by most of the shorter of the two, or it touches the band, as the half bars of
                // the two tracks of a two-track Pharmacode do.
                int overlap = Math.Min(bottom, candidate.Bottom) - Math.Max(top, candidate.Top) + 1;
                int shorter = Math.Min(candidate.Height, bottom - top + 1);
                if (overlap * 5 < shorter * 2 && (overlap < -2 || distance > (width * 4) + 2))
                {
                    continue;
                }

                if (pitch > 0 && MathF.Abs(distance - pitch) > pitch * 0.35F)
                {
                    continue;
                }

                next = j;
                break;
            }

            if (next < 0)
            {
                return;
            }

            Component bar = bars[next];
            if (pitch == 0)
            {
                pitch = bar.CenterX - last.CenterX;
            }

            top = Math.Min(top, bar.Top);
            bottom = Math.Max(bottom, bar.Bottom);
            row.Add(next);
            current = next;
        }
    }

    /// <summary>
    /// Gives the bars of a row their states and passes them to the readers upright and upside down.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bars">The bars.</param>
    /// <param name="row">The bars of the row, from the left.</param>
    /// <param name="bits">The image in this orientation.</param>
    /// <param name="turned">Whether the image is the quarter turn of the source.</param>
    /// <param name="readers">The readers.</param>
    private static void ReadRow(
        DecodeContext context,
        List<Component> bars,
        List<int> row,
        BitImage bits,
        bool turned,
        ReadOnlySpan<HeightModulatedReader> readers)
    {
        int top = int.MaxValue;
        int bottom = int.MinValue;
        foreach (int index in row)
        {
            top = Math.Min(top, bars[index].Top);
            bottom = Math.Max(bottom, bars[index].Bottom);
        }

        float tolerance = (bottom - top + 1) * ExtenderTolerance;
        Span<FourState> states = stackalloc FourState[row.Count];
        Span<FourState> flipped = stackalloc FourState[row.Count];
        for (int i = 0; i < row.Count; i++)
        {
            Component bar = bars[row[i]];
            bool ascender = bar.Top <= top + tolerance;
            bool descender = bar.Bottom >= bottom - tolerance;
            states[i] = (ascender ? FourState.Ascender : FourState.Tracker) | (descender ? FourState.Descender : FourState.Tracker);

            // Upside down the bars read from the other end, and an ascender is a descender.
            flipped[row.Count - 1 - i] = (descender ? FourState.Ascender : FourState.Tracker) | (ascender ? FourState.Descender : FourState.Tracker);
        }

        Component first = bars[row[0]];
        Component end = bars[row[^1]];
        foreach (HeightModulatedReader reader in readers)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool upsideDown = pass == 1;
                if (upsideDown && reader.Directional)
                {
                    break;
                }

                if (reader.Directional && turned)
                {
                    break;
                }

                if (!reader.TryRead(upsideDown ? flipped : states, context, out BarcodeFamily family, out string? text, out string? identifier)
                    || !context.Wants(family))
                {
                    continue;
                }

                // The corners as the symbol reads: an upside down symbol starts at the bottom right.
                PointF topLeft = Map(first.Left, top, bits, turned);
                PointF topRight = Map(end.Right + 1, top, bits, turned);
                PointF bottomRight = Map(end.Right + 1, bottom + 1, bits, turned);
                PointF bottomLeft = Map(first.Left, bottom + 1, bits, turned);
                PointF[] corners = upsideDown ? [bottomRight, bottomLeft, topLeft, topRight] : [topLeft, topRight, bottomRight, bottomLeft];
                context.Add(family, text, identifier, corners);
            }
        }
    }

    /// <summary>
    /// Maps a point of the scanned orientation to the source image. The quarter turn takes the source pixel edge (x, y) to (h - y, x), where h is the height of
    /// the source, the width of the turned image.
    /// </summary>
    /// <param name="x">The column in the scanned orientation.</param>
    /// <param name="y">The row in the scanned orientation.</param>
    /// <param name="bits">The image in this orientation.</param>
    /// <param name="turned">Whether the image is the quarter turn of the source.</param>
    /// <returns>The point in the source image.</returns>
    private static PointF Map(float x, float y, BitImage bits, bool turned)
        => turned ? new PointF(y, bits.Width - x) : new PointF(x, y);
}
