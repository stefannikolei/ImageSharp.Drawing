// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Channel Code, ANSI/AIM BC12-1998: a finder pattern of one-module elements, then a space and a bar for each of three to eight channels. The channels
/// of C channels add to 4C - 2 modules, and the value is the position of their widths in the order of the symbology, which
/// <see cref="ChannelCodeEncoder.Rank"/> gives. The value transmits as C - 1 digits.
/// </summary>
internal sealed class ChannelCodeReader : LinearReader
{
    private const int MinimumChannels = 3;
    private const int MaximumChannels = 8;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.ChannelCode;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<int> spaces = stackalloc int[MaximumChannels];
        Span<int> bars = stackalloc int[MaximumChannels];
        for (int i = start; i + 5 < runs.Length; i += 2)
        {
            // The full finder pattern is nine elements and the short one five. The full pattern ends in a short one, so either reads the same channels.
            foreach (int finder in (ReadOnlySpan<int>)[9, 5])
            {
                if (i + finder >= runs.Length)
                {
                    continue;
                }

                float module = Sum(runs, i, finder) / (float)finder;
                if (!AllAbout(runs.Slice(i, finder), module) || !IsQuiet(runs, i - 1, module * 0.8F))
                {
                    continue;
                }

                for (int channels = MaximumChannels; channels >= MinimumChannels; channels--)
                {
                    int first = i + finder;
                    int end = first + (channels * 2);
                    if (end > runs.Length || !IsQuiet(runs, end, module * 1.5F))
                    {
                        continue;
                    }

                    // The channels fill 4C - 2 modules.
                    float total = Sum(runs, first, channels * 2) / module;
                    if (MathF.Abs(total - ((channels * 4) - 2)) > channels * 0.5F)
                    {
                        continue;
                    }

                    int modules = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        spaces[c] = (int)MathF.Round(runs[first + (c * 2)] / module);
                        bars[c] = (int)MathF.Round(runs[first + (c * 2) + 1] / module);
                        modules += spaces[c] + bars[c];
                    }

                    if (modules != (channels * 4) - 2)
                    {
                        continue;
                    }

                    int value = ChannelCodeEncoder.Rank(spaces[..channels], bars[..channels]);
                    if (value < 0 || value > ChannelCodeEncoder.MaximumValue(channels - 1))
                    {
                        continue;
                    }

                    string text = value.ToString(CultureInfo.InvariantCulture).PadLeft(channels - 1, '0');
                    match = new LinearMatch(BarcodeFamily.ChannelCode, text, "]X0", i, end, false);
                    return true;
                }
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Returns a value indicating whether every run is about one module.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> when every run is one module.</returns>
    private static bool AllAbout(ReadOnlySpan<int> runs, float module)
    {
        foreach (int run in runs)
        {
            if (run < module * 0.5F || run > module * 1.5F)
            {
                return false;
            }
        }

        return true;
    }
}
