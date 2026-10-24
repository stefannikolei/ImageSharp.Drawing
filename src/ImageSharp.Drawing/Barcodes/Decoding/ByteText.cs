// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Turns the bytes a symbol carries into text, by the character set an Extended Channel Interpretation names. Without an ECI the bytes read as the default
/// character set of the symbologies, ISO/IEC 8859-1, unless they form UTF-8 with at least one character of more than one byte, as text encoded by a modern
/// system does.
/// </summary>
internal static class ByteText
{
    /// <summary>
    /// The strict UTF-8 decoder, which throws on an invalid sequence.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Decodes bytes.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="eci">The ECI number, or -1 for none.</param>
    /// <returns>The text.</returns>
    public static string Decode(ReadOnlySpan<byte> bytes, int eci)
    {
        Encoding? encoding = eci < 0 ? null : EncodingOf(eci);
        if (encoding is not null)
        {
            return encoding.GetString(bytes);
        }

        if (eci < 0 && bytes.ContainsAnyExceptInRange((byte)0, (byte)0x7F))
        {
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Not UTF-8: the bytes read as ISO/IEC 8859-1 below.
            }
        }

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Returns the character set of an ECI number of the AIM ECI register, or <see langword="null"/> for ISO/IEC 8859-1 and for a number the decoder does not
    /// know.
    /// </summary>
    /// <param name="eci">The ECI number.</param>
    /// <returns>The encoding.</returns>
    private static Encoding? EncodingOf(int eci)
    {
        int codePage = eci switch
        {
            0 or 2 => 437,
            4 => 28592,
            5 => 28593,
            6 => 28594,
            7 => 28595,
            8 => 28596,
            9 => 28597,
            10 => 28598,
            11 => 28599,
            15 => 28603,
            17 => 28605,
            20 => 932,
            21 => 1250,
            22 => 1251,
            23 => 1252,
            24 => 1256,
            25 => 1201,
            26 => 65001,
            27 or 170 => 20127,
            28 => 950,
            29 => 936,
            30 => 949,
            32 => 54936,
            33 => 1200,
            34 => 12000,
            35 => 12001,
            _ => 0,
        };

        return codePage switch
        {
            0 => null,
            65001 => Encoding.UTF8,
            1200 => Encoding.Unicode,
            1201 => Encoding.BigEndianUnicode,
            12000 => Encoding.UTF32,
            12001 => new UTF32Encoding(true, false),
            20127 => Encoding.ASCII,
            _ => CodePagesEncodingProvider.Instance.GetEncoding(codePage),
        };
    }
}
