// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Code 11. Each character is three bars and two spaces, one or two of them wide, followed by a narrow gap, and the start and stop character is the same
/// pattern. The reader transmits the characters between the start and stop character, the check characters included. A last character that is the C check
/// character of the data, or two that are the C and K check characters, give the symbol its check.
/// </summary>
internal sealed class Code11Reader : LinearReader
{
    /// <summary>
    /// The value of the start and stop character.
    /// </summary>
    private const int StartStop = 11;

    /// <summary>
    /// The character value of each five element pattern, the first element in the lowest bit, or -1.
    /// </summary>
    private static readonly sbyte[] Characters = Interleaved2Of5Reader.CreateDigits(Code11Encoder.Patterns);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Code11;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<char> characters = stackalloc char[(runs.Length / 6) + 1];
        for (int i = start; i + 5 < runs.Length; i += 2)
        {
            if (ReadCharacter(runs.Slice(i, 5)) != StartStop)
            {
                continue;
            }

            int width = Sum(runs, i, 5);
            if (!IsQuiet(runs, i - 1, width))
            {
                continue;
            }

            int count = 0;
            int position = i + 6;
            bool stopped = false;
            while (position + 5 <= runs.Length && count < characters.Length && runs[position - 1] <= width / 2)
            {
                int value = ReadCharacter(runs.Slice(position, 5));
                if (value < 0)
                {
                    break;
                }

                if (value == StartStop)
                {
                    stopped = count > 0 && IsQuiet(runs, position + 5, width);
                    position += 5;
                    break;
                }

                characters[count++] = Code11Encoder.Characters[value];
                position += 6;
            }

            if (stopped)
            {
                ReadOnlySpan<char> data = characters[..count];
                bool checkK = data.Length > 2 && Code11Encoder.CheckK(data[..^2], Code11Encoder.CheckC(data[..^2])) == Code11Encoder.Value(data[^1])
                    && Code11Encoder.CheckC(data[..^2]) == Code11Encoder.Value(data[^2]);
                bool checkC = data.Length > 1 && Code11Encoder.CheckC(data[..^1]) == Code11Encoder.Value(data[^1]);
                match = new LinearMatch(BarcodeFamily.Code11, data.ToString(), checkK ? "]H1" : "]H0", i, position, checkC || checkK);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads one character from five runs: one or two of them wide.
    /// </summary>
    /// <param name="runs">The five runs, bar first.</param>
    /// <returns>The character value, or -1.</returns>
    private static int ReadCharacter(ReadOnlySpan<int> runs)
    {
        int mask = WideElements(runs, 1);
        if (mask >= 0 && Characters[mask] >= 0)
        {
            return Characters[mask];
        }

        mask = WideElements(runs, 2);
        return mask >= 0 ? Characters[mask] : -1;
    }
}
