using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class UnreadClickGateTests
{
    [Fact]
    public void BaselineMarker_RemainsLockedWhenOcrTextDrifts()
    {
        var gate = new UnreadClickGate();
        gate.Initialize([Marker("公众号", "旧消息", 200)]);

        Assert.Empty(gate.Observe([Marker("医刷", "旧消息", 201)]));
        Assert.Empty(gate.Observe([Marker("族刷", "旧消息", 199)]));
        Assert.Empty(gate.Observe([Marker("公众号", "旧消息", 200)]));
    }

    [Fact]
    public void NewMarker_RequiresThreeIdenticalFramesAndFiresOnce()
    {
        var gate = new UnreadClickGate();
        gate.Initialize([]);
        var marker = Marker("张三", "你好", 180);

        Assert.Empty(gate.Observe([marker]));
        Assert.Empty(gate.Observe([marker with { RowY = 182 }]));
        Assert.Single(gate.Observe([marker with { RowY = 181 }]));
        Assert.Empty(gate.Observe([marker]));
    }

    [Fact]
    public void ExistingMarker_WhenUnlocked_RequiresThreeStableFramesAndFiresOnce()
    {
        var gate = new UnreadClickGate();
        var marker = Marker("测试联系人", "你好", 261);
        gate.Initialize([marker], locked: false);

        Assert.Empty(gate.Observe([marker]));
        Assert.Single(gate.Observe([marker]));
        Assert.Empty(gate.Observe([marker]));
    }

    [Fact]
    public void NewMarker_NameOrPreviewDriftResetsStability()
    {
        var gate = new UnreadClickGate();
        gate.Initialize([]);

        Assert.Empty(gate.Observe([Marker("张三", "你好", 180)]));
        Assert.Empty(gate.Observe([Marker("张二", "你好", 181)]));
        Assert.Empty(gate.Observe([Marker("张三", "您好", 180)]));
        Assert.Empty(gate.Observe([Marker("张三", "你好", 180)]));
    }

    [Fact]
    public void AmbiguousLayoutShift_QuarantinesUnmatchedMarkers()
    {
        var gate = new UnreadClickGate();
        gate.Initialize([Marker(string.Empty, string.Empty, 180)]);

        Assert.Empty(gate.Observe([Marker("张三", "你好", 250)]));
        Assert.Empty(gate.Observe([Marker("张三", "你好", 250)]));
        Assert.Empty(gate.Observe([Marker("张三", "你好", 250)]));
    }

    [Fact]
    public void MutedMarker_NeverBecomesReady()
    {
        var gate = new UnreadClickGate();
        gate.Initialize([]);
        var marker = new UnreadSession("项目群（12）", "新消息", 180, IsMuted: true);

        Assert.Empty(gate.Observe([marker]));
        Assert.Empty(gate.Observe([marker]));
        Assert.Empty(gate.Observe([marker]));
        Assert.Empty(gate.Observe([marker]));
    }

    [Fact]
    public void MuteStateChange_ResetsStability()
    {
        var gate = new UnreadClickGate();
        gate.Initialize([]);
        var marker = Marker("项目群", "新消息", 180);

        Assert.Empty(gate.Observe([marker]));
        Assert.Empty(gate.Observe([marker with { IsMuted = true }]));
        Assert.Empty(gate.Observe([marker]));
        Assert.Empty(gate.Observe([marker]));
        Assert.Single(gate.Observe([marker]));
    }

    private static UnreadSession Marker(string contact, string preview, int rowY) =>
        new(contact, preview, rowY);
}
