using System;
using System.Collections.Generic;
using System.Linq;

namespace FileCrawler.Utilities;

/// <summary>
/// Helpers for filtering user-facing lists with forgiving, case-insensitive text search.
/// </summary>
public static class UserSearchHelpers
{
    /// <summary>Splits a search query on whitespace, dropping empties.</summary>
    internal static IReadOnlyList<string> ExtractSearchTerms(string? query) {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    /// <summary>
    /// True when <paramref name="target"/> matches every search term, order-independently.
    /// </summary>
    /// <remarks>
    /// A term matches when it is a case-insensitive substring of <paramref name="target"/> or when it can be
    /// segmented into prefixes of distinct target words. For example, "gepaba" matches "Get Packed Bags".
    /// Empty search term lists always match.
    /// </remarks>
    internal static bool MatchesAllSearchTerms(IReadOnlyList<string> searchTerms, string target) {
        if (searchTerms.Count == 0) return true;
        var targetWords = ExtractTargetWords(target);
        return searchTerms.All(t => MatchesSearchTerm(t, target, targetWords));
    }

    /// <summary>
    /// Returns every item whose display string matches all terms in <paramref name="query"/>.<br/>
    /// Runs in O(n*m) time where n = number of whitespace separated tokens in the query, and m = number of items.<br/>
    /// This is intended for usecases where a query is O(10) tokens and there are O(1000) items. For vastly larger searches, a different search strategy is recommended.
    /// </summary>
    /// <remarks>
    /// Results are returned in the same order that they were provided. TODO: consider sorting results by relevance.
    /// </remarks>
    public static IEnumerable<T> FindAllMatches<T>(string query, IEnumerable<T> items, Func<T, string> itemToString) {
        var searchTerms = ExtractSearchTerms(query);
        return items.Where(i => MatchesAllSearchTerms(searchTerms, itemToString(i)));
    }

    /// <summary>Splits a search target into words that may be used for compact prefix matching.</summary>
    private static IReadOnlyList<string> ExtractTargetWords(string target) {
        return target.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// True when a single search term matches the target by substring or compact target-word prefixes.
    /// </summary>
    private static bool MatchesSearchTerm(string searchTerm, string target, IReadOnlyList<string> targetWords) {
        return target.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
               || MatchesTargetWordPrefixes(searchTerm, targetWords);
    }

    /// <summary>
    /// True when <paramref name="searchTerm"/> can be fully consumed by prefixes of distinct target words.
    /// </summary>
    /// <remarks>
    /// The matcher tries unused words in any order and, for each word, tries possible prefix lengths from longest to
    /// shortest. In the worst case it explores permutations of target words and prefix lengths, so runtime is
    /// exponential: O(W! * L^W), where W is the target word count and L is the search term length. Search targets here
    /// are short UI labels, so this favors predictable matching behavior over a more complex dynamic-programming cache.
    /// </remarks>
    private static bool MatchesTargetWordPrefixes(string searchTerm, IReadOnlyList<string> targetWords) {
        var targetWordCount = targetWords.Count;
        if (targetWordCount <= 0) return false;
        if (targetWordCount > 6) return false; // this algorithm is insanely slow with respect to target word count, so let's not even bother if the word count is even moderately long

        // this algorithm can also blow up if target word count is small but the search term is long, so let's protect against that too
        var lTermComplexity = Math.Pow(searchTerm.Length, targetWordCount);
        if (lTermComplexity > 1e5) return false; // 15 character search term ^ 4 target words gives 5e4. Double that seems like a reasonable upper bound for the L^W part of the time complexity
        return MatchesTargetWordPrefixes(searchTerm, targetWords, usedWordIndexes: [], searchTermIndex: 0);
    }

    private static bool MatchesTargetWordPrefixes(
        string searchTerm,
        IReadOnlyList<string> targetWords,
        HashSet<int> usedWordIndexes,
        int searchTermIndex
    ) {
        if (searchTermIndex == searchTerm.Length) return true;

        for (var wordIndex = 0; wordIndex < targetWords.Count; wordIndex++) {
            if (usedWordIndexes.Contains(wordIndex)) continue;
            var word = targetWords[wordIndex];
            var maxPrefixLength = Math.Min(word.Length, searchTerm.Length - searchTermIndex);

            for (var prefixLength = maxPrefixLength; prefixLength > 0; prefixLength--) {
                if (!searchTerm.AsSpan(searchTermIndex, prefixLength).Equals(word.AsSpan(0, prefixLength), StringComparison.OrdinalIgnoreCase)) continue;

                usedWordIndexes.Add(wordIndex);
                if (MatchesTargetWordPrefixes(searchTerm, targetWords, usedWordIndexes, searchTermIndex + prefixLength)) return true;
                usedWordIndexes.Remove(wordIndex);
            }
        }

        return false;
    }
}
