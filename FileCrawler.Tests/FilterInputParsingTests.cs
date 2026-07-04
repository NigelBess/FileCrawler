using FileCrawler.Utilities;
using Xunit;

namespace FileCrawler.Tests;

public class FilterInputParsingTests
{
    [Theory]
    [InlineData("png, .JPG ; gif", new[] { ".png", ".jpg", ".gif" })]
    [InlineData("*.psd", new[] { ".psd" })]
    [InlineData("  txt   txt ", new[] { ".txt" })] // duplicates collapse
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData(". ; ,", new string[0])] // junk-only input
    [InlineData("c:\\bad png", new[] { ".png" })] // path-like tokens dropped
    public void ParseExtensions_normalizes_and_drops_junk(string input, string[] expected)
    {
        Assert.Equal(expected, FilterInputParser.ParseExtensions(input));
    }

    [Fact]
    public void TryParseSize_empty_is_no_bound_not_invalid()
    {
        Assert.False(FilterInputParser.TryParseSize("", SizeUnit.MB, out _, out var invalid));
        Assert.False(invalid);
    }

    [Theory]
    [InlineData("1", SizeUnit.B, 1L)]
    [InlineData("1", SizeUnit.KB, 1024L)]
    [InlineData("1.5", SizeUnit.GB, 1610612736L)] // 1.5 * 1024^3
    public void TryParseSize_scales_by_unit(string text, SizeUnit unit, long expectedBytes)
    {
        Assert.True(FilterInputParser.TryParseSize(text, unit, out var bytes, out var invalid));
        Assert.Equal(expectedBytes, bytes);
        Assert.False(invalid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    public void TryParseSize_garbage_is_flagged_invalid(string text)
    {
        Assert.False(FilterInputParser.TryParseSize(text, SizeUnit.MB, out _, out var invalid));
        Assert.True(invalid);
    }
}
