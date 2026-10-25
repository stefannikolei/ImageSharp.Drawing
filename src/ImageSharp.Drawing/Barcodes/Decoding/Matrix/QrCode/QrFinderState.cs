// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The finder patterns of one decode, found once and shared by the QR Code, Micro QR Code and Rectangular Micro QR Code readers, with the patterns a symbol
/// already took.
/// </summary>
internal sealed class QrFinderState
{
    private readonly HashSet<FinderPattern> used = [];
    private List<FinderPattern>? patterns;

    /// <summary>
    /// Returns the finder patterns of the decode's image, found on first use.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <returns>The patterns.</returns>
    public List<FinderPattern> Patterns(DecodeContext context)
    {
        if (this.patterns is null)
        {
            BitImage bits = context.Bits;
            int step = context.Options.TryHarder ? 1 : Math.Max(1, bits.Height / 256);
            this.patterns = FinderPatternFinder.Find(bits, step);
        }

        return this.patterns;
    }

    /// <summary>
    /// Returns whether a symbol took a pattern.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns><see langword="true"/> when a symbol took it.</returns>
    public bool IsUsed(FinderPattern pattern) => this.used.Contains(pattern);

    /// <summary>
    /// Marks a pattern as taken by a symbol.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    public void Use(FinderPattern pattern) => this.used.Add(pattern);
}
