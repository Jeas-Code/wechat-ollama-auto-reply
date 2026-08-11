using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class ConversationComposerTests
{
    [Fact]
    public void IncomingTextMessages_OnlyKeepsOtherPeoplesText()
    {
        var now = DateTime.UtcNow;
        var messages = new[]
        {
            new MessageSnapshot("张三", "你好", true, now),
            new MessageSnapshot("我", "你好呀", true, now),
            new MessageSnapshot("系统", "以上是打招呼的内容", true, now),
            new MessageSnapshot("张三", "[图片]", false, now)
        };

        var result = ConversationComposer.IncomingTextMessages(messages);

        var message = Assert.Single(result);
        Assert.Equal("你好", message.Text);
    }

    [Fact]
    public void Build_MapsRolesAndLimitsHistory()
    {
        var now = DateTime.UtcNow;
        var history = new[]
        {
            new MessageSnapshot("张三", "一", true, now),
            new MessageSnapshot("我", "二", true, now),
            new MessageSnapshot("张三", "三", true, now)
        };
        var incoming = new[] { new MessageSnapshot("张三", "四", true, now) };

        var result = ConversationComposer.Build("规则", history, incoming, maxHistoryMessages: 2);

        Assert.Collection(
            result,
            item => Assert.Equal(new OllamaMessage("system", "规则"), item),
            item => Assert.Equal(new OllamaMessage("assistant", "二"), item),
            item => Assert.Equal(new OllamaMessage("user", "三"), item),
            item => Assert.Equal(new OllamaMessage("user", "四"), item));
    }

    [Fact]
    public void Build_DoesNotDuplicateIncomingSuffixAlreadyInHistory()
    {
        var now = DateTime.UtcNow;
        var incoming = new[] { new MessageSnapshot("张三", "在吗", true, now) };

        var result = ConversationComposer.Build("规则", incoming, incoming, maxHistoryMessages: 10);

        Assert.Equal(2, result.Count);
        Assert.Equal("在吗", result[1].Content);
    }
}
