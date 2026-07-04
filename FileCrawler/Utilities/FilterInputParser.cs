using System;
using System.Collections.Generic;
using System.Globalization;

namespace FileCrawler.Utilities;

/// <summary>Units for the size filter inputs. Multipliers are 1024-based, matching <see cref="SizeFormatter"/>.</summary>
public enum SizeUnit { B, KB, MB, GB }

/// <summary>
/// Forgiving parsers for the free-text filter inputs. Bad tokens are dropped or ignored rather than
/// erroring — the filter bar should never block a search.
/// </summary>
public static class FilterInputParser
{
    private static readonly char[] ExtensionSeparators = [' ', ',', ';', '\t'];

    /// <summary>
    /// Parses user-typed extensions ("psd, .BLEND kra") into normalized lowercase dot-prefixed
    /// extensions ({".psd", ".blend", ".kra"}). Junk tokens (bare dots, path separators) are dropped.
    /// </summary>
    public static IReadOnlyList<string> ParseExtensions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var raw in text.Split(ExtensionSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim().TrimStart('.', '*');
            if (token.Length == 0) continue;
            if (token.IndexOfAny(['\\', '/', ':', '"', '<', '>', '|', '?', '*', '.']) >= 0) continue;

            var normalized = "." + token.ToLowerInvariant();
            if (!result.Contains(normalized)) result.Add(normalized);
        }
        return result;
    }

    /// <summary>
    /// Parses a size bound like "1.5" with <paramref name="unit"/> into bytes. Returns false for
    /// non-numeric or negative input; empty/whitespace input is not an error and also returns false —
    /// callers treat both as "no bound" (distinguish via <paramref name="isInvalid"/>).
    /// </summary>
    public static bool TryParseSize(string? text, SizeUnit unit, out long bytes, out bool isInvalid)
    {
        bytes = 0;
        isInvalid = false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || value < 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            isInvalid = true;
            return false;
        }

        var scaled = value * Multiplier(unit);
        bytes = scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
        return true;
    }

    private static double Multiplier(SizeUnit unit) => unit switch
    {
        SizeUnit.B => 1,
        SizeUnit.KB => 1024,
        SizeUnit.MB => 1024 * 1024,
        SizeUnit.GB => 1024L * 1024 * 1024,
        _ => 1,
    };
}
