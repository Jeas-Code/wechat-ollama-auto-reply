using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using RapidOCRLib;
using RapidOCRLib.Models;

namespace WeChatOllamaAutoReply;

public sealed record UnreadSession(string Contact, string Preview, int RowY)
{
    public string Key => $"{VisualMessagePolicy.Normalize(Contact)}|{VisualMessagePolicy.Normalize(Preview)}";
}

public sealed class VisualWeChatClient : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly Window _window;
    private readonly OcrLite _ocr;

    private VisualWeChatClient(UIA3Automation automation, Window window, OcrLite ocr)
    {
        _automation = automation;
        _window = window;
        _ocr = ocr;
    }

    public static async Task<VisualWeChatClient> CreateAsync(AppOptions options, CancellationToken cancellationToken)
    {
        var modelPaths = options.GetRequiredOcrModelPaths();
        var ocr = new OcrLite
        {
            DetPath = modelPaths["ch_PP-OCRv5_mobile_det.onnx"],
            ClsPath = modelPaths["ch_ppocr_mobile_v2.0_cls_infer.onnx"],
            RecPath = modelPaths["ch_PP-OCRv5_rec_mobile_infer.onnx"],
            KeyDicPath = modelPaths["ppocrv5_dict.txt"],
            ThreadNum = Math.Max(1, (int)(Environment.ProcessorCount * 0.7))
        };
        await ocr.InitModels();
        cancellationToken.ThrowIfCancellationRequested();

        var processIds = Process.GetProcessesByName("Weixin").Select(process => process.Id).ToHashSet();
        var automation = new UIA3Automation();
        var candidates = automation.GetDesktop()
            .FindAllChildren(condition => condition.ByControlType(ControlType.Window))
            .Where(element => processIds.Contains(element.Properties.ProcessId.Value))
            .Where(element => element.BoundingRectangle.Width >= 600 && element.BoundingRectangle.Height >= 450)
            .Select(element => element.AsWindow())
            .OrderByDescending(window => window.BoundingRectangle.Width * window.BoundingRectangle.Height)
            .ToArray();

        if (candidates.Length == 0)
        {
            automation.Dispose();
            var message = processIds.Count > 0
                ? "检测到 Weixin.exe，但微信窗口已最小化或隐藏。请恢复主窗口并保持可见。"
                : "没有检测到 Weixin.exe。请先打开并登录电脑版微信。";
            throw new InvalidOperationException(message);
        }

        if (candidates.Select(window => window.Properties.ProcessId.Value).Distinct().Count() > 1)
        {
            automation.Dispose();
            throw new InvalidOperationException("检测到多个可见微信窗口。为避免误发，请只保留一个微信窗口。");
        }

        return new VisualWeChatClient(automation, candidates[0], ocr);
    }

    public async Task<int> CheckAsync(CancellationToken cancellationToken)
    {
        using var bitmap = Capture();
        var result = await DetectAsync(bitmap, cancellationToken);
        try
        {
            return result.TextBlocks.Count(block => !string.IsNullOrWhiteSpace(block.Text));
        }
        finally
        {
            result.BoxImg?.Dispose();
        }
    }

    public async Task<IReadOnlyList<UnreadSession>> DetectUnreadSessionsAsync(CancellationToken cancellationToken)
    {
        using var bitmap = Capture();
        var layout = GetLayout(bitmap.Size);
        var badges = RedBadgeDetector.Find(bitmap, layout.BadgeSearchArea);
        if (badges.Count == 0)
        {
            return [];
        }

        using var sessionBitmap = bitmap.Clone(layout.SessionArea, bitmap.PixelFormat);
        var ocr = await DetectAsync(sessionBitmap, cancellationToken);
        TextBox[] blocks;
        try
        {
            blocks = ocr.TextBlocks
                .Where(block => !string.IsNullOrWhiteSpace(block.Text))
                .Select(block => new TextBox(block.Text.Trim(), CenterX(block), CenterY(block) + layout.SessionArea.Top))
                .ToArray();
        }
        finally
        {
            ocr.BoxImg?.Dispose();
        }

        var sessions = new List<UnreadSession>();
        foreach (var badge in badges)
        {
            var rowBlocks = blocks
                .Where(block => Math.Abs(block.Y - badge.Y) <= layout.RowHalfHeight)
                .Where(block => block.X < layout.SessionArea.Width * 0.82)
                .OrderBy(block => block.Y)
                .ThenBy(block => block.X)
                .ToArray();
            if (rowBlocks.Length == 0)
            {
                continue;
            }

            var contact = rowBlocks[0].Text;
            var preview = string.Join(" ", rowBlocks.Skip(1).Select(block => block.Text)).Trim();
            if (!string.IsNullOrWhiteSpace(contact))
            {
                sessions.Add(new UnreadSession(contact, preview, badge.Y));
            }
        }

        return sessions.GroupBy(session => session.Key).Select(group => group.First()).ToArray();
    }

    public async Task<string> OpenAndReadTitleAsync(UnreadSession session, CancellationToken cancellationToken)
    {
        _window.Focus();
        var bounds = _window.BoundingRectangle;
        var clickX = bounds.X + (int)(bounds.Width * 0.22);
        Mouse.Click(new Point(clickX, bounds.Y + session.RowY));
        await Task.Delay(700, cancellationToken);

        using var bitmap = Capture();
        var layout = GetLayout(bitmap.Size);
        using var header = bitmap.Clone(layout.HeaderArea, bitmap.PixelFormat);
        var result = await DetectAsync(header, cancellationToken);
        try
        {
            return result.TextBlocks
                .Where(block => !string.IsNullOrWhiteSpace(block.Text))
                .OrderBy(block => CenterY(block))
                .ThenBy(block => CenterX(block))
                .Select(block => block.Text.Trim())
                .FirstOrDefault() ?? string.Empty;
        }
        finally
        {
            result.BoxImg?.Dispose();
        }
    }

    public void SendText(string text, bool sendWithCtrlEnter)
    {
        _window.Focus();
        var bounds = _window.BoundingRectangle;
        var inputPoint = new Point(
            bounds.X + (int)(bounds.Width * 0.70),
            bounds.Y + (int)(bounds.Height * 0.78));
        Mouse.Click(inputPoint);
        SetClipboardText(text);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        if (sendWithCtrlEnter)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.RETURN);
        }
        else
        {
            Keyboard.Type(VirtualKeyShort.RETURN);
        }
    }

    public void Dispose()
    {
        _automation.Dispose();
    }

    private Bitmap Capture()
    {
        _window.Focus();
        var bitmap = _window.Capture();
        if (bitmap.Width < 600 || bitmap.Height < 450)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("微信窗口过小或已隐藏，请恢复窗口后重试。");
        }

        return bitmap;
    }

    private async Task<OcrResult> DetectAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _ocr.DetectAsync(
            bitmap,
            padding: 0,
            maxSideLen: Math.Max(bitmap.Width, bitmap.Height),
            boxScoreThresh: 0.45f,
            boxThresh: 0.3f,
            unClipRatio: 1.6f,
            doAngle: false,
            mostAngle: false);
    }

    private static void SetClipboardText(string text)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new InvalidOperationException("写入剪贴板失败。", error);
        }
    }

    private static VisualLayout GetLayout(Size size)
    {
        var sessionLeft = (int)(size.Width * 0.065);
        var sessionRight = (int)(size.Width * 0.382);
        var top = (int)(size.Height * 0.11);
        return new VisualLayout(
            new Rectangle(sessionLeft, top, sessionRight - sessionLeft, size.Height - top),
            new Rectangle(sessionLeft + 25, top, Math.Min(75, sessionRight - sessionLeft - 25), size.Height - top),
            new Rectangle(sessionRight, 0, size.Width - sessionRight, Math.Max(60, top)),
            Math.Max(25, (int)(size.Height * 0.045)));
    }

    private static int CenterX(TextBlock block) => (block.BoxPoints[0].X + block.BoxPoints[2].X) / 2;
    private static int CenterY(TextBlock block) => (block.BoxPoints[0].Y + block.BoxPoints[2].Y) / 2;

    private sealed record TextBox(string Text, int X, int Y);
    private sealed record VisualLayout(Rectangle SessionArea, Rectangle BadgeSearchArea, Rectangle HeaderArea, int RowHalfHeight);
}
