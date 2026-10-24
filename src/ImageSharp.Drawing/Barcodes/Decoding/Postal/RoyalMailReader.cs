// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the Royal Mail 4-State Customer Code and KIX, whose characters are four bars of <see cref="RoyalMailEncoder.Patterns"/>, two with an ascender and two
/// with a descender. RM4SCC has an ascender start bar, a check character and a full stop bar, which the reader checks and does not transmit. KIX is the
/// characters alone, and a KIX symbol upside down is another valid KIX symbol, so the KIX reader reads the upright image only.
/// </summary>
internal sealed class RoyalMailReader : HeightModulatedReader
{
    /// <summary>
    /// The character value of every four bar pattern, the first bar in the highest two bits, or -1.
    /// </summary>
    private static readonly sbyte[] Characters = CreateCharacters();

    private readonly bool kix;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoyalMailReader"/> class.
    /// </summary>
    /// <param name="kix">Whether the reader reads KIX rather than RM4SCC.</param>
    public RoyalMailReader(bool kix) => this.kix = kix;

    /// <inheritdoc/>
    public override BarcodeFamily Families => this.kix ? BarcodeFamily.Kix : BarcodeFamily.RoyalMail;

    /// <inheritdoc/>
    public override bool Directional => this.kix;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier)
    {
        family = BarcodeFamily.None;
        text = string.Empty;
        identifier = "]X0";
        int bars = RoyalMailEncoder.BarsPerCharacter;
        bool royalMail = !this.kix;
        if (royalMail ? states.Length % bars != 2 || states[0] != FourState.Ascender || states[^1] != FourState.Full : states.Length % bars != 0)
        {
            return false;
        }

        ReadOnlySpan<FourState> characters = royalMail ? states[1..^1] : states;
        int count = characters.Length / bars;
        if (count < (royalMail ? 2 : 1))
        {
            return false;
        }

        Span<char> data = stackalloc char[count];
        for (int c = 0; c < count; c++)
        {
            int pattern = 0;
            for (int b = 0; b < bars; b++)
            {
                pattern = (pattern << 2) | (int)characters[(c * bars) + b];
            }

            int value = Characters[pattern];
            if (value < 0)
            {
                return false;
            }

            data[c] = RoyalMailEncoder.Characters[value];
        }

        if (royalMail)
        {
            if (RoyalMailEncoder.CheckCharacter(data[..^1]) != data[^1])
            {
                return false;
            }

            family = BarcodeFamily.RoyalMail;
            text = data[..^1].ToString();
            return true;
        }

        family = BarcodeFamily.Kix;
        text = data.ToString();
        return true;
    }

    /// <summary>
    /// Inverts <see cref="RoyalMailEncoder.Patterns"/>.
    /// </summary>
    /// <returns>The character value of every pattern.</returns>
    private static sbyte[] CreateCharacters()
    {
        sbyte[] characters = new sbyte[256];
        characters.AsSpan().Fill(-1);
        ReadOnlySpan<byte> patterns = RoyalMailEncoder.Patterns;
        int bars = RoyalMailEncoder.BarsPerCharacter;
        for (int v = 0; v < patterns.Length / bars; v++)
        {
            int pattern = 0;
            for (int b = 0; b < bars; b++)
            {
                pattern = (pattern << 2) | patterns[(v * bars) + b];
            }

            characters[pattern] = (sbyte)v;
        }

        return characters;
    }
}
