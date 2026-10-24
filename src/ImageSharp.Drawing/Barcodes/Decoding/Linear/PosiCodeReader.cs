// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads PosiCode, AIM ITS/02-001, in its versions A, B, Limited A and Limited B. Every bar is one module, and the data lies in the spaces: a data character
/// is three bars and three spaces of <see cref="PosiCodeEncoder.CharactersA"/> or <see cref="PosiCodeEncoder.CharactersLimitedA"/>, with one module more in
/// every space for version B. The check character before the stop character is twelve runs that <see cref="PosiCodeEncoder.AppendCheck"/> gives. The
/// codewords of the standard versions read through the character sets, latches, shifts and function 4 characters of <see cref="PosiCodeEncoder.ToCodewords"/>.
/// </summary>
internal sealed class PosiCodeReader : LinearReader
{
    private const int RunsPerCharacter = 6;
    private const int CheckRuns = 12;

    /// <summary>
    /// The message value of every codeword of every set of the standard versions, at index (set * 64) + codeword: an ASCII value, 128 for function 4, or -1.
    /// </summary>
    private static readonly short[] Messages = CreateMessages();

    /// <summary>
    /// The versions in the order the reader tries them.
    /// </summary>
    private static readonly PosiCodeVersion[] Versions = [PosiCodeVersion.A, PosiCodeVersion.B, PosiCodeVersion.LimitedA, PosiCodeVersion.LimitedB];

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.PosiCode;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<int> codewords = stackalloc int[(runs.Length / RunsPerCharacter) + 1];
        Span<int> check = stackalloc int[CheckRuns];
        for (int i = start; i + 6 < runs.Length; i += 2)
        {
            foreach (PosiCodeVersion version in Versions)
            {
                ReadOnlySpan<byte> startPattern = StartPattern(version);
                if (i + startPattern.Length >= runs.Length)
                {
                    continue;
                }

                // Every element of the start character is a whole number of modules, the bars one each.
                float module = runs[i];
                if (!Matches(runs.Slice(i, startPattern.Length), startPattern, 0, module)
                    || !IsQuiet(runs, i - 1, module * PosiCodeEncoder.QuietZone(version) / 2F))
                {
                    continue;
                }

                if (TryReadFrom(runs, i + startPattern.Length, version, module, codewords, check, out int count, out int end)
                    && TryInterpret(codewords[..count], version, out string? text))
                {
                    match = new LinearMatch(BarcodeFamily.PosiCode, text, "]X0", i, end, true);
                    return true;
                }
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads the data characters until the check character and the stop character follow, and checks the check character.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first bar after the start character.</param>
    /// <param name="version">The version.</param>
    /// <param name="module">The module width.</param>
    /// <param name="codewords">The codewords to write.</param>
    /// <param name="check">Scratch for the twelve check runs.</param>
    /// <param name="count">The number of codewords.</param>
    /// <param name="end">The index of the light run after the stop character.</param>
    /// <returns><see langword="true"/> when the symbol reads and checks.</returns>
    private static bool TryReadFrom(
        ReadOnlySpan<int> runs,
        int position,
        PosiCodeVersion version,
        float module,
        Span<int> codewords,
        Span<int> check,
        out int count,
        out int end)
    {
        count = 0;
        end = 0;
        bool limited = PosiCodeEncoder.IsLimited(version);
        ReadOnlySpan<byte> characters = limited ? PosiCodeEncoder.CharactersLimitedA : PosiCodeEncoder.CharactersA;
        ReadOnlySpan<byte> stop = StopPattern(version);
        int offset = PosiCodeEncoder.SpaceOffset(version);
        float quiet = module * PosiCodeEncoder.QuietZone(version) / 2F;
        while (count < codewords.Length)
        {
            // The check character and the stop character can follow every data character.
            int stopAt = position + CheckRuns;
            if (count > 0 && stopAt + stop.Length <= runs.Length && Matches(runs.Slice(stopAt, stop.Length), stop, 0, module)
                && IsQuiet(runs, stopAt + stop.Length, quiet))
            {
                PosiCodeEncoder.AppendCheck(codewords[..count], version, check);
                if (Matches(runs.Slice(position, CheckRuns), check, module))
                {
                    end = stopAt + stop.Length;
                    return true;
                }
            }

            if (position + RunsPerCharacter > runs.Length)
            {
                return false;
            }

            int codeword = -1;
            int codewordCount = characters.Length / RunsPerCharacter;
            ReadOnlySpan<int> character = runs.Slice(position, RunsPerCharacter);
            for (int c = 0; c < codewordCount; c++)
            {
                if (Matches(character, characters.Slice(c * RunsPerCharacter, RunsPerCharacter), offset, module))
                {
                    codeword = c;
                    break;
                }
            }

            if (codeword < 0)
            {
                return false;
            }

            codewords[count++] = codeword;
            position += RunsPerCharacter;
        }

        return false;
    }

    /// <summary>
    /// Turns the codewords into the text: the limited character set directly, or the character sets of the standard versions.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="version">The version.</param>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the codewords are a valid message.</returns>
    private static bool TryInterpret(ReadOnlySpan<int> codewords, PosiCodeVersion version, out string text)
    {
        text = string.Empty;
        Span<char> characters = stackalloc char[codewords.Length];
        int count = 0;
        if (PosiCodeEncoder.IsLimited(version))
        {
            // The limited codewords are the first codewords of set 0.
            foreach (int codeword in codewords)
            {
                characters[count++] = (char)Messages[codeword];
            }

            text = characters[..count].ToString();
            return true;
        }

        int set = 0;
        bool extended = false;
        bool announced = false;
        for (int i = 0; i < codewords.Length; i++)
        {
            int codeword = codewords[i];
            int currentSet = set;
            switch (codeword)
            {
                case PosiCodeEncoder.LatchCodeword:
                    set = 1 - set;
                    continue;
                case PosiCodeEncoder.ShiftCodeword:
                case PosiCodeEncoder.ShiftToSet2Codeword:
                    if (++i >= codewords.Length)
                    {
                        return false;
                    }

                    currentSet = codeword == PosiCodeEncoder.ShiftCodeword ? 1 - set : 2;
                    codeword = codewords[i];
                    break;
            }

            int message = Messages[(currentSet * 64) + codeword];
            if (message < 0)
            {
                return false;
            }

            // A function 4 character announces one character above 127. Two in a row switch the mode for the characters that follow.
            if (message == PosiCodeEncoder.Function4)
            {
                if (announced)
                {
                    extended = !extended;
                    announced = false;
                }
                else
                {
                    announced = true;
                }

                continue;
            }

            bool high = extended ^ announced;
            announced = false;
            characters[count++] = (char)(high ? message + 128 : message);
        }

        text = characters[..count].ToString();
        return count > 0;
    }

    /// <summary>
    /// Returns a value indicating whether runs equal a pattern of module widths, each width plus the offset on the spaces, at one module each.
    /// </summary>
    /// <param name="runs">The runs, bar first.</param>
    /// <param name="pattern">The module widths.</param>
    /// <param name="offset">The modules added to every space.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> when every run rounds to its width.</returns>
    private static bool Matches(ReadOnlySpan<int> runs, ReadOnlySpan<byte> pattern, int offset, float module)
    {
        for (int r = 0; r < pattern.Length; r++)
        {
            int width = pattern[r] + ((r & 1) * offset);
            if (MathF.Abs((runs[r] / module) - width) > 0.45F)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns a value indicating whether runs equal a pattern of module widths.
    /// </summary>
    /// <param name="runs">The runs, bar first.</param>
    /// <param name="pattern">The module widths.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> when every run rounds to its width.</returns>
    private static bool Matches(ReadOnlySpan<int> runs, ReadOnlySpan<int> pattern, float module)
    {
        for (int r = 0; r < pattern.Length; r++)
        {
            if (MathF.Abs((runs[r] / module) - pattern[r]) > 0.45F)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the start character of a version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The run widths.</returns>
    private static ReadOnlySpan<byte> StartPattern(PosiCodeVersion version) => version switch
    {
        PosiCodeVersion.A => PosiCodeEncoder.StartA,
        PosiCodeVersion.B => PosiCodeEncoder.StartB,
        PosiCodeVersion.LimitedA => PosiCodeEncoder.StartLimitedA,
        _ => PosiCodeEncoder.StartLimitedB,
    };

    /// <summary>
    /// Returns the stop character of a version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The run widths.</returns>
    private static ReadOnlySpan<byte> StopPattern(PosiCodeVersion version) => version switch
    {
        PosiCodeVersion.A => PosiCodeEncoder.StopA,
        PosiCodeVersion.B => PosiCodeEncoder.StopB,
        _ => PosiCodeEncoder.StopLimited,
    };

    /// <summary>
    /// Inverts <see cref="PosiCodeEncoder.CharacterCodes"/>: each entry is the set above the codeword bits.
    /// </summary>
    /// <returns>The message value of every set and codeword.</returns>
    private static short[] CreateMessages()
    {
        short[] messages = new short[3 * 64];
        messages.AsSpan().Fill(-1);
        ReadOnlySpan<byte> codes = PosiCodeEncoder.CharacterCodes;
        for (int value = 0; value < codes.Length; value++)
        {
            int code = codes[value];
            int set = code >> PosiCodeEncoder.CodewordBitCount;
            int codeword = code & ((1 << PosiCodeEncoder.CodewordBitCount) - 1);
            messages[(set * 64) + codeword] = (short)value;
        }

        return messages;
    }
}
