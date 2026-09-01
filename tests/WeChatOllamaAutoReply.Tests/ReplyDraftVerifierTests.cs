using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class ReplyDraftVerifierTests
{
    [Fact]
    public void ContainsReply_ExactMatch_Passes()
    {
        Assert.True(ReplyDraftVerifier.ContainsReply("好的，我明天回复你", "好的，我明天回复你"));
    }

    [Fact]
    public void ContainsReply_IgnoresWhitespaceAndLineBreaks()
    {
        Assert.True(ReplyDraftVerifier.ContainsReply("好的，\n我明天 回复你", "好的，我明天回复你"));
    }

    [Fact]
    public void ContainsReply_ToleratesPunctuationLossFromOcr()
    {
        Assert.True(ReplyDraftVerifier.ContainsReply("好的我明天回复你", "好的，我明天回复你。"));
    }

    [Fact]
    public void ContainsReply_LongReply_AllowsPartialOcrDamageViaFragments()
    {
        var reply = "好的，我明天下午三点之前把整理好的文件发给你。";
        // OCR 只可靠识别出尾部片段，头部与中部均已损坏。
        Assert.True(ReplyDraftVerifier.ContainsReply("…识别噪声…之前把整理好的文件发给你", reply));
    }

    [Fact]
    public void ContainsReply_LongReply_RejectsWhenNoFragmentSurvives()
    {
        var reply = "好的，我明天下午三点之前把整理好的文件发给你。";
        Assert.False(ReplyDraftVerifier.ContainsReply("完全不同的输入框内容", reply));
    }

    [Fact]
    public void ContainsReply_ShortReply_RequiresFullMatch()
    {
        Assert.True(ReplyDraftVerifier.ContainsReply("发送消息 好的收到", "好的收到"));
        Assert.False(ReplyDraftVerifier.ContainsReply("发送消息 好的", "好的收到"));
    }

    [Fact]
    public void ContainsReply_EmptyInput_FailsClosed()
    {
        Assert.False(ReplyDraftVerifier.ContainsReply(string.Empty, "你好"));
        Assert.False(ReplyDraftVerifier.ContainsReply("你好", string.Empty));
        Assert.False(ReplyDraftVerifier.ContainsReply("   ", "你好"));
    }

    [Fact]
    public void ContainsReply_OnlyAlphanumericComparison_StillRejectsWrongText()
    {
        Assert.False(ReplyDraftVerifier.ContainsReply("发送消息 明天再说", "今天必须发出"));
    }
}
