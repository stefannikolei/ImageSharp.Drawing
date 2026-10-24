// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads BC412, SEMI T1-95. Every bar is one module, and each character is four bars with spaces of one to five modules that add to twelve modules, the
/// spaces of <see cref="Bc412Encoder.Spaces"/>. The start character is a bar and a space of two modules and the stop character a bar, a space and a bar of one
/// module. The second character of the symbol is the check character, which the reader checks and does not transmit.
/// </summary>
internal sealed class Bc412Reader : LinearReader
{
    /// <summary>
    /// The character value of each set of four spaces, at index ((s0 * 5 + s1) * 5 + s2) * 5 + s3 for the spaces less one, or -1.
    /// </summary>
    private static readonly sbyte[] Characters = CreateCharacters();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Bc412;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<char> carried = stackalloc char[Bc412Encoder.MaximumLength + 1];
        Span<char> buffer = stackalloc char[Bc412Encoder.MaximumLength];
        for (int i = start; i + 2 < runs.Length; i += 2)
        {
            // The start character: a bar of one module and a space of two. The bars give the module.
            float module = runs[i];
            float startSpace = runs[i + 1] / module;
            if (startSpace < 1.5F || startSpace > 2.5F || !IsQuiet(runs, i - 1, module * 3))
            {
                continue;
            }

            int count = 0;
            int position = i + 2;
            while (position + 3 <= runs.Length)
            {
                // The stop character: a bar, a space and a bar of one module, then the quiet zone. The quiet zone of four modules is narrower than the widest
                // space of a character, so a character can start the same way, and the stop character only ends a symbol whose check character holds.
                if (IsModule(runs[position], module) && IsModule(runs[position + 1], module) && IsModule(runs[position + 2], module)
                    && IsQuiet(runs, position + 3, module * 3)
                    && TryCheck(carried[..count], buffer, out string? text))
                {
                    match = new LinearMatch(BarcodeFamily.Bc412, text, "]X0", i, position + 3, true);
                    return true;
                }

                if (position + 8 > runs.Length || count >= carried.Length)
                {
                    break;
                }

                int index = 0;
                bool valid = true;
                for (int s = 0; s < 4 && valid; s++)
                {
                    int space = (int)MathF.Round(runs[position + (s * 2) + 1] / module);
                    valid = IsModule(runs[position + (s * 2)], module) && space is >= 1 and <= 5;
                    index = (index * 5) + space - 1;
                }

                if (!valid || Characters[index] < 0)
                {
                    break;
                }

                carried[count++] = Bc412Encoder.Characters[Characters[index]];
                position += 8;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Checks the carried characters: the first data character, the check character and the rest of the data.
    /// </summary>
    /// <param name="carried">The carried characters.</param>
    /// <param name="buffer">Scratch for the data.</param>
    /// <param name="text">The data.</param>
    /// <returns><see langword="true"/> when the check character holds.</returns>
    private static bool TryCheck(ReadOnlySpan<char> carried, Span<char> buffer, out string text)
    {
        text = string.Empty;
        if (carried.Length < Bc412Encoder.MinimumLength + 1 || carried.Length > Bc412Encoder.MaximumLength + 1)
        {
            return false;
        }

        Span<char> data = buffer[..(carried.Length - 1)];
        data[0] = carried[0];
        carried[2..].CopyTo(data[1..]);
        if (Bc412Encoder.CheckCharacter(data) != carried[Bc412Encoder.CheckPosition])
        {
            return false;
        }

        text = data.ToString();
        return true;
    }

    /// <summary>
    /// Returns a value indicating whether a run is one module.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> for a run of one module.</returns>
    private static bool IsModule(int run, float module) => run >= module * 0.5F && run <= module * 1.5F;

    /// <summary>
    /// Indexes the space sets of <see cref="Bc412Encoder.Spaces"/>.
    /// </summary>
    /// <returns>The character value of every set.</returns>
    private static sbyte[] CreateCharacters()
    {
        sbyte[] characters = new sbyte[5 * 5 * 5 * 5];
        characters.AsSpan().Fill(-1);
        ReadOnlySpan<byte> spaces = Bc412Encoder.Spaces;
        for (int v = 0; v < spaces.Length / 4; v++)
        {
            int index = 0;
            for (int s = 0; s < 4; s++)
            {
                index = (index * 5) + spaces[(v * 4) + s] - 1;
            }

            characters[index] = (sbyte)v;
        }

        return characters;
    }
}
