// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The regions of dark pixels of one decode: each the bounding box that grows from a dark pixel while a dark pixel lies within a gap beyond one of its sides.
/// A symbol whose quiet zone is wider than the gap is one region, so the matrix readers without a finder pattern of their own look for a symbol in each. The
/// regions of each gap are found once and shared.
/// </summary>
internal sealed class SymbolRegions
{
    private readonly Dictionary<int, List<Rectangle>> regions = [];

    /// <summary>
    /// Returns the regions of the decode's image for a gap.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="gap">The widest light gap in pixels inside a region.</param>
    /// <returns>The regions.</returns>
    public List<Rectangle> Find(DecodeContext context, int gap)
    {
        if (!this.regions.TryGetValue(gap, out List<Rectangle>? found))
        {
            found = Find(context.Bits, gap, context.Options.TryHarder);
            this.regions.Add(gap, found);
        }

        return found;
    }

    /// <summary>
    /// Finds the regions from the dark pixels of a grid of seeds.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="gap">The widest light gap in pixels inside a region.</param>
    /// <param name="tryHarder">Whether the seeds are denser.</param>
    /// <returns>The regions.</returns>
    private static List<Rectangle> Find(BitImage bits, int gap, bool tryHarder)
    {
        List<Rectangle> found = [];
        int step = Math.Max(1, Math.Min(bits.Width, bits.Height) / (tryHarder ? 128 : 48));
        for (int y = 0; y < bits.Height; y += step)
        {
            for (int x = 0; x < bits.Width; x += step)
            {
                if (!bits[x, y] || Contains(found, x, y))
                {
                    continue;
                }

                Rectangle region = Grow(bits, x, y, gap);
                if (region.Width >= 8 && region.Height >= 8)
                {
                    found.Add(region);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Returns whether a region holds a pixel.
    /// </summary>
    /// <param name="regions">The regions.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns><see langword="true"/> when a region holds the pixel.</returns>
    private static bool Contains(List<Rectangle> regions, int x, int y)
    {
        foreach (Rectangle region in regions)
        {
            if (region.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Grows the bounding box of a dark pixel until no dark pixel lies within the gap beyond any side.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="x">The x coordinate of the pixel.</param>
    /// <param name="y">The y coordinate of the pixel.</param>
    /// <param name="gap">The widest light gap in pixels inside the region.</param>
    /// <returns>The region.</returns>
    private static Rectangle Grow(BitImage bits, int x, int y, int gap)
    {
        int left = x;
        int right = x;
        int top = y;
        int bottom = y;
        bool grown = true;
        while (grown)
        {
            grown = false;
            while (right + 1 < bits.Width && AnyDark(bits, right + 1, Math.Min(bits.Width - 1, right + gap), top, bottom))
            {
                right++;
                grown = true;
            }

            while (bottom + 1 < bits.Height && AnyDark(bits, left, right, bottom + 1, Math.Min(bits.Height - 1, bottom + gap)))
            {
                bottom++;
                grown = true;
            }

            while (left > 0 && AnyDark(bits, Math.Max(0, left - gap), left - 1, top, bottom))
            {
                left--;
                grown = true;
            }

            while (top > 0 && AnyDark(bits, left, right, Math.Max(0, top - gap), top - 1))
            {
                top--;
                grown = true;
            }
        }

        // The box ends at the last dark pixel on each side, not at the gap.
        while (right > left && !AnyDark(bits, right, right, top, bottom))
        {
            right--;
        }

        while (bottom > top && !AnyDark(bits, left, right, bottom, bottom))
        {
            bottom--;
        }

        while (left < right && !AnyDark(bits, left, left, top, bottom))
        {
            left++;
        }

        while (top < bottom && !AnyDark(bits, left, right, top, top))
        {
            top++;
        }

        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// Returns whether a block of pixels holds a dark pixel.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="left">The first column.</param>
    /// <param name="right">The last column.</param>
    /// <param name="top">The first row.</param>
    /// <param name="bottom">The last row.</param>
    /// <returns><see langword="true"/> when a pixel is dark.</returns>
    private static bool AnyDark(BitImage bits, int left, int right, int top, int bottom)
    {
        for (int row = top; row <= bottom; row++)
        {
            ReadOnlySpan<byte> pixels = bits.Row(row).Slice(left, right - left + 1);
            if (pixels.Contains((byte)1))
            {
                return true;
            }
        }

        return false;
    }
}
