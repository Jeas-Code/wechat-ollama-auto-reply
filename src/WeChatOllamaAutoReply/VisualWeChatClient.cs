using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
    public string Key => VisualMessagePolicy.ContactKey(Contact);
}

public sealed record VisualCheckResult(Size WindowSize, int TextBlockCount, IReadOnlyList<Point> BadgeCandidates);

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

        var processes = Process.GetProcessesByName("Weixin");
        var processIds = processes.Select(process => process.Id).ToHashSet();
        var automation = new UIA3Automation();
        var handles = FindVisibleTopLevelWindows(processIds);
        if (handles.Count == 0)
        {
            var restorableHandle = FindRestorableTopLevelWindow(processIds);
            if (restorableHandle != IntPtr.Zero)
            {
                ShowWindow(restorableHandle, ShowWindowCommand.Restore);
                SetForegroundWindow(restorableHandle);
                await Task.Delay(800, cancellationToken);
                handles = FindVisibleTopLevelWindows(processIds);
            }
        }

        if (handles.Count == 0)
        {
            Keyboard.TypeSimultaneously(
                VirtualKeyShort.CONTROL,
                VirtualKeyShort.ALT,
                VirtualKeyShort.KEY_W);
            await Task.Delay(800, cancellationToken);
            handles = FindVisibleTopLevelWindows(processIds);
        }

        var candidates = handles
            .Select(handle => automation.FromHandle(handle).AsWindow())
            .OrderByDescending(window =>
            {
                var bounds = GetNativeBounds(window.Properties.NativeWindowHandle.Value);
                return bounds.Width * bounds.Height;
            })
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

    public async Task<VisualCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var bitmap = Capture();
        var debugCapturePath = Environment.GetEnvironmentVariable("AICHAT_DEBUG_CAPTURE");
        if (!string.IsNullOrWhiteSpace(debugCapturePath))
        {
            bitmap.Save(Path.GetFullPath(debugCapturePath));
        }
        var layout = GetLayout(bitmap.Size);
        var badges = RedBadgeDetector.Find(bitmap, layout.BadgeSearchArea);
        var result = await DetectAsync(bitmap, cancellationToken);
        try
        {
            return new VisualCheckResult(
                bitmap.Size,
                result.TextBlocks.Count(block => !string.IsNullOrWhiteSpace(block.Text)),
                badges);
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
            var titleOffset = rowBlocks[0].Y - badge.Y;
            if (titleOffset is < 4 or > 24)
            {
                continue;
            }

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
        var bounds = GetNativeBounds();
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
        var bounds = GetNativeBounds();
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
        var bounds = GetNativeBounds();
        if (bounds.Width < 300 || bounds.Height < 250)
        {
            throw new InvalidOperationException("微信窗口过小或已隐藏，请恢复窗口后重试。");
        }

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private Rectangle GetNativeBounds()
    {
        return GetNativeBounds(_window.Properties.NativeWindowHandle.Value);
    }

    private static Rectangle GetNativeBounds(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法获取微信窗口句柄。");
        }

        NativeRect rect;
        var result = DwmGetWindowAttribute(
            handle,
            DwmWindowAttribute.ExtendedFrameBounds,
            out rect,
            Marshal.SizeOf<NativeRect>());
        if (result != 0 && !GetWindowRect(handle, out rect))
        {
            throw new InvalidOperationException("无法获取微信窗口位置。");
        }

        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static IReadOnlyList<IntPtr> FindVisibleTopLevelWindows(IReadOnlySet<int> processIds)
    {
        var handles = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            if (!processIds.Contains((int)processId) || !IsWindowVisible(handle))
            {
                return true;
            }

            try
            {
                var bounds = GetNativeBounds(handle);
                if (bounds.Width >= 300 && bounds.Height >= 250)
                {
                    handles.Add(handle);
                }
            }
            catch (InvalidOperationException)
            {
                // Ignore transient helper windows that disappear during enumeration.
            }

            return true;
        }, IntPtr.Zero);
        return handles;
    }

    private static IntPtr FindRestorableTopLevelWindow(IReadOnlySet<int> processIds)
    {
        var candidates = new List<(IntPtr Handle, long Area)>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            if (!processIds.Contains((int)processId))
            {
                return true;
            }

            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(handle, ref placement))
            {
                return true;
            }

            var width = placement.NormalPosition.Right - placement.NormalPosition.Left;
            var height = placement.NormalPosition.Bottom - placement.NormalPosition.Top;
            if (width >= 300 && height >= 250)
            {
                candidates.Add((handle, (long)width * height));
            }

            return true;
        }, IntPtr.Zero);

        return candidates.OrderByDescending(candidate => candidate.Area)
            .Select(candidate => candidate.Handle)
            .FirstOrDefault();
    }

    private async Task<OcrResult> DetectAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await OcrConsoleLock.WaitAsync(cancellationToken);
        var originalOutput = Console.Out;
        try
        {
            Console.SetOut(TextWriter.Null);
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
        finally
        {
            Console.SetOut(originalOutput);
            OcrConsoleLock.Release();
        }
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
        var sessionLeft = (int)(size.Width * 0.10);
        var sessionRight = (int)(size.Width * 0.39);
        var top = (int)(size.Height * 0.15);
        var badgeLeft = Math.Clamp((int)(size.Width * 0.173), sessionLeft + 18, sessionRight - 16);
        var badgeWidth = Math.Max(12, (int)(size.Width * 0.037));
        return new VisualLayout(
            new Rectangle(sessionLeft, top, sessionRight - sessionLeft, size.Height - top),
            new Rectangle(badgeLeft, top, Math.Min(badgeWidth, sessionRight - badgeLeft), size.Height - top),
            new Rectangle(sessionRight, 0, size.Width - sessionRight, Math.Max(60, top)),
            Math.Max(22, (int)(size.Height * 0.065)));
    }

    private static int CenterX(TextBlock block) => (block.BoxPoints[0].X + block.BoxPoints[2].X) / 2;
    private static int CenterY(TextBlock block) => (block.BoxPoints[0].Y + block.BoxPoints[2].Y) / 2;

    private sealed record TextBox(string Text, int X, int Y);
    private sealed record VisualLayout(Rectangle SessionArea, Rectangle BadgeSearchArea, Rectangle HeaderArea, int RowHalfHeight);
    private static readonly SemaphoreSlim OcrConsoleLock = new(1, 1);

    private enum DwmWindowAttribute
    {
        ExtendedFrameBounds = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    private enum ShowWindowCommand
    {
        Restore = 9
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        DwmWindowAttribute attribute,
        out NativeRect value,
        int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr windowHandle, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, ShowWindowCommand command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
