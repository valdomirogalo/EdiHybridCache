using Xunit;
using EdiHybridCache.Cache;

namespace EdiHybridCache.Tests;

public class ConstantsTests
{
    [Fact]
    public void SanitizeForLog_ShouldReplaceNewlines()
    {
        Assert.Equal("hello world !", Constants.SanitizeForLog("hello\r\nworld\n!"));
        Assert.Equal("a b c", Constants.SanitizeForLog("a\rb\rc"));
    }

    [Fact]
    public void SanitizeForLog_WithoutNewlines_ShouldReturnUnchanged()
    {
        Assert.Equal("plain-key", Constants.SanitizeForLog("plain-key"));
    }

    [Fact]
    public void MeterName_MatchesConstant()
    {
        Assert.Equal("EdiHybridCache", Constants.MeterName);
    }
}
