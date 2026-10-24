// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Corrects the errors in a Reed-Solomon code word: the syndromes, the Berlekamp-Massey algorithm for the error locator, the Chien search for its roots and
/// the Forney algorithm for the error values. The arithmetic comes from a field type, so one implementation serves the binary fields GF(2^m) of the matrix
/// symbologies and the prime fields of PDF417, DotCode and Ultracode. The field type is a struct, so the JIT compiles a copy of the decoder for each field and
/// the field operations inline.
/// </summary>
internal static class ReedSolomonDecoder
{
    /// <summary>
    /// The largest number of check symbols a code word has: PDF417 at error correction level 8 has 512.
    /// </summary>
    private const int MaximumCheckCount = 512;

    /// <summary>
    /// Corrects a code word in place. The code word is the data symbols followed by the check symbols, the first symbol as the highest term of the code word
    /// polynomial. The polynomial is a multiple of the generator (x - a^r)(x - a^(r+1)) ... (x - a^(r+k-1)), where a is the primitive element and r is the first
    /// root.
    /// </summary>
    /// <typeparam name="TField">The field arithmetic.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="codeword">The code word to correct.</param>
    /// <param name="checkCount">The number of check symbols, k.</param>
    /// <param name="firstRoot">The power of the primitive element of the first root of the generator, r.</param>
    /// <returns>The number of symbols corrected, or -1 when the code word has more errors than the code corrects.</returns>
    public static int Correct<TField>(TField field, Span<int> codeword, int checkCount, int firstRoot)
        where TField : struct, IReedSolomonField
    {
        int n = codeword.Length;
        if (checkCount <= 0)
        {
            return 0;
        }

        if (checkCount > MaximumCheckCount || n > field.Order)
        {
            return -1;
        }

        // Syndrome j is the code word polynomial at a^(r+j). A code word with no errors is a multiple of the generator, so every syndrome is zero. Horner's
        // rule evaluates the polynomial from its highest term down.
        Span<int> syndromes = stackalloc int[checkCount];
        bool clean = true;
        for (int j = 0; j < checkCount; j++)
        {
            int x = field.Exp(firstRoot + j);
            int value = 0;
            for (int i = 0; i < n; i++)
            {
                value = field.Add(field.Multiply(value, x), codeword[i]);
            }

            syndromes[j] = value;
            clean &= value == 0;
        }

        if (clean)
        {
            return 0;
        }

        // The Berlekamp-Massey algorithm finds the shortest linear feedback shift register that generates the syndromes. Its connection polynomial is the
        // error locator: sigma(x) = (1 - X1 x)(1 - X2 x) ... for the error locators Xi = a^pi, where pi is the power of the term in error. Each step computes
        // the discrepancy of the register against the next syndrome and corrects the register with the copy saved at the last length change.
        Span<int> sigma = stackalloc int[checkCount + 1];
        Span<int> previous = stackalloc int[checkCount + 1];
        Span<int> scratch = stackalloc int[checkCount + 1];
        sigma.Clear();
        previous.Clear();
        sigma[0] = 1;
        previous[0] = 1;
        int length = 0;
        int shift = 1;
        int previousDiscrepancy = 1;
        for (int step = 0; step < checkCount; step++)
        {
            int discrepancy = syndromes[step];
            for (int i = 1; i <= length; i++)
            {
                discrepancy = field.Add(discrepancy, field.Multiply(sigma[i], syndromes[step - i]));
            }

            if (discrepancy == 0)
            {
                shift++;
                continue;
            }

            // sigma(x) -= (d / b) x^m B(x), where B is the register saved at the last length change and b its discrepancy.
            int factor = field.Multiply(discrepancy, field.Inverse(previousDiscrepancy));
            bool lengthens = 2 * length <= step;
            if (lengthens)
            {
                sigma.CopyTo(scratch);
            }

            for (int i = 0; i + shift <= checkCount; i++)
            {
                sigma[i + shift] = field.Subtract(sigma[i + shift], field.Multiply(factor, previous[i]));
            }

            if (lengthens)
            {
                length = step + 1 - length;
                scratch.CopyTo(previous);
                previousDiscrepancy = discrepancy;
                shift = 1;
            }
            else
            {
                shift++;
            }
        }

        // A code with k check symbols corrects at most k / 2 errors.
        if (2 * length > checkCount)
        {
            return -1;
        }

        // The Chien search tries every term of the code word: the term of power p is in error when sigma(a^-p) is zero. The code word has as many roots as
        // the locator has degree, or the errors are more than the code corrects.
        Span<int> powers = stackalloc int[length];
        int found = 0;
        for (int p = 0; p < n && found < length; p++)
        {
            int x = field.Exp(field.Order - p);
            int value = 0;
            for (int i = length; i >= 0; i--)
            {
                value = field.Add(field.Multiply(value, x), sigma[i]);
            }

            if (value == 0)
            {
                powers[found++] = p;
            }
        }

        if (found != length)
        {
            return -1;
        }

        // The error evaluator omega(x) = S(x) sigma(x) mod x^k, where S(x) is the syndrome polynomial with syndrome j at x^j.
        Span<int> omega = stackalloc int[checkCount];
        for (int i = 0; i < checkCount; i++)
        {
            int value = 0;
            for (int j = 0; j <= Math.Min(i, length); j++)
            {
                value = field.Add(value, field.Multiply(sigma[j], syndromes[i - j]));
            }

            omega[i] = value;
        }

        // The Forney algorithm gives the value of the error at locator X: e = -X^(1-r) omega(X^-1) / sigma'(X^-1), where sigma' is the formal derivative.
        // The derivative of the term c x^i is i c x^(i-1), where i c is c added i times, which in a binary field drops the terms of even i.
        for (int e = 0; e < length; e++)
        {
            int p = powers[e];
            int inverse = field.Exp(field.Order - p);
            int omegaValue = 0;
            for (int i = checkCount - 1; i >= 0; i--)
            {
                omegaValue = field.Add(field.Multiply(omegaValue, inverse), omega[i]);
            }

            int derivative = 0;
            int xPower = 1;
            for (int i = 1; i <= length; i++)
            {
                derivative = field.Add(derivative, field.Multiply(field.Scale(sigma[i], i), xPower));
                xPower = field.Multiply(xPower, inverse);
            }

            if (derivative == 0)
            {
                return -1;
            }

            // The received symbol is the code symbol plus e, so the correction adds X^(1-r) omega(X^-1) / sigma'(X^-1), which is -e.
            int locatorPower = field.Exp(((((1 - firstRoot) * p) % field.Order) + field.Order) % field.Order);
            int magnitude = field.Multiply(field.Multiply(locatorPower, omegaValue), field.Inverse(derivative));
            int index = n - 1 - p;
            codeword[index] = field.Add(codeword[index], magnitude);
        }

        return length;
    }
}
