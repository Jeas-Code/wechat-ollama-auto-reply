using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class UnreadRowMatcherTests
{
    [Theory]
    [InlineData(235, false)]
    [InlineData(291, true)]
    [InlineData(315, true)]
    [InlineData(333, false)]
    public void IsInRow_UsesOnlyBlocksBelowBadge(int blockY, bool expected)
    {
        Assert.Equal(expected, UnreadRowMatcher.IsInRow(blockY, badgeY: 279, rowHalfHeight: 53));
    }
}
