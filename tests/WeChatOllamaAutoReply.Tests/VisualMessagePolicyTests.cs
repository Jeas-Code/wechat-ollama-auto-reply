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

    [Theory]
    [InlineData("诗诗老婆..", "诗诗老婆")]
    [InlineData("永远偏爱°", "永远偏爱")]
    [InlineData("  张 三  ", "张三")]
    public void ContactKey_RemovesOcrPunctuationNoise(string input, string expected)
    {
        Assert.Equal(expected, VisualMessagePolicy.ContactKey(input));
    }

    [Theory]
    [InlineData("张三", "张三", true)]
    [InlineData("张三..", "张三", true)]
    [InlineData("张三", "张三丰", false)]
    [InlineData("医刷", "族刷", false)]
    [InlineData("平", "平", false)]
    public void SameContact_RequiresExactNormalizedName(string expected, string actual, bool result)
    {
        Assert.Equal(result, VisualMessagePolicy.SameContact(expected, actual));
    }

    [Theory]
    [InlineData("张三", "张三", true)]
    [InlineData("项目讨论群...", "项目讨论群（12）", true)]
    [InlineData("项目讨论群", "另一个群（12）", false)]
    [InlineData("公众号", "公众号", true)]
    public void ConversationMatchesList_SupportsExactPrivateAndTruncatedGroup(
        string listTitle,
        string openedTitle,
        bool expected)
    {
        Assert.Equal(expected, VisualMessagePolicy.ConversationMatchesList(listTitle, openedTitle));
    }

    [Fact]
    public void IsSupportedConversation_AllowsGroupsButRejectsSystemChats()
    {
        IReadOnlySet<string> empty = new HashSet<string>();

        Assert.True(VisualMessagePolicy.IsSupportedConversation("项目讨论群（12）", empty, true));
        Assert.True(VisualMessagePolicy.IsSupportedConversation("张三", empty, true));
        Assert.False(VisualMessagePolicy.IsSupportedConversation("公众号", empty, true));
        Assert.False(VisualMessagePolicy.IsSupportedConversation("服务通知", empty, true));
    }

    [Theory]
    [InlineData("公众号", true)]
    [InlineData("服务号", true)]
    [InlineData("服务通知", true)]
    [InlineData("张三", false)]
    [InlineData("项目群（12）", false)]
    public void IsSystemChat_BlocksKnownListEntriesBeforeClick(string title, bool expected)
    {
        Assert.Equal(expected, VisualMessagePolicy.IsSystemChat(title));
    }

    [Theory]
    [InlineData("项目群（12）", "项目群（13）", true)]
    [InlineData("项目群（12）", "项目群（12）", true)]
    [InlineData("项目群（12）", "其他群（12）", false)]
    public void SameConversationTitle_IgnoresOnlyGroupMemberCount(
        string first,
        string second,
        bool expected)
    {
        Assert.Equal(expected, VisualMessagePolicy.SameConversationTitle(first, second));
    }
}
