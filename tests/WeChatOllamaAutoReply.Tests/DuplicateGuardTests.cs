using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class DuplicateGuardTests
{
    [Fact]
    public void TryAccept_RejectsSameCallbackWithinRetention()
    {
        var guard = new DuplicateGuard(TimeSpan.FromMinutes(10));
        var now = DateTimeOffset.UtcNow;
        var messages = new[] { new MessageSnapshot("张三", "你好", true, now.UtcDateTime) };

        Assert.True(guard.TryAccept("张三", messages, now));
        Assert.False(guard.TryAccept("张三", messages, now.AddMinutes(1)));
    }

    [Fact]
    public void TryAccept_AllowsSameMessageAfterRetention()
    {
        var guard = new DuplicateGuard(TimeSpan.FromMinutes(10));
        var now = DateTimeOffset.UtcNow;
        var messages = new[] { new MessageSnapshot("张三", "你好", true, now.UtcDateTime) };

        Assert.True(guard.TryAccept("张三", messages, now));
        Assert.True(guard.TryAccept("张三", messages, now.AddMinutes(11)));
    }
}
