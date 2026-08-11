using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class VisualMessagePolicyTests
{
    [Theory]
    [InlineData("朋友小王", true)]
    [InlineData("项目讨论群（12）", false)]
    [InlineData("测试群(3)", false)]
    [InlineData("公众号", false)]
    [InlineData("微信支付", false)]
    public void IsLikelyPrivateChat_RejectsGroupsAndSystemChats(string title, bool expected)
    {
        Assert.Equal(expected, VisualMessagePolicy.IsLikelyPrivateChat(title, new HashSet<string>()));
    }

    [Theory]
    [InlineData("晚上有空吗？", true)]
    [InlineData("[图片]", false)]
    [InlineData("【语音】", false)]
    [InlineData("[文件]", false)]
    public void IsPlainTextPreview_OnlyAllowsText(string preview, bool expected)
    {
        Assert.Equal(expected, VisualMessagePolicy.IsPlainTextPreview(preview));
    }

    [Fact]
    public void AllowedContacts_ActsAsWhitelist()
    {
        IReadOnlySet<string> allowed = new HashSet<string> { "张三" };

        Assert.True(VisualMessagePolicy.IsLikelyPrivateChat("张三", allowed));
        Assert.False(VisualMessagePolicy.IsLikelyPrivateChat("李四", allowed));
    }
}
