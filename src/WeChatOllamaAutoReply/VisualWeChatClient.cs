using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
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

public sealed record UnreadSession(
    string Contact,
    string Preview,
    int RowY,
    bool IsMuted = false,
    Rectangle WindowBounds = default,
    int SessionLeft = 0,
    int SessionRight = 0)
{
    public string Key => VisualMessagePolicy.ContactKey(Contact);
}

public sealed record WeChatWindowProbe(
    Rectangle CaptureBounds,
    Rectangle InputBounds,
    int SessionLeft,
    int SessionRight);

public sealed record VisualCheckResult(
    Size WindowSize,
    int TextBlockCount,
    IReadOnlyList<Point> BadgeCandidates,
    IReadOnlyList<Point> MutedBadgeCandidates);

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

        var titledHandles = handles
            .Where(handle =>
            {
                var nativeTitle = GetWindowTitle(handle);
                if (string.Equals(nativeTitle, "微信", StringComparison.Ordinal))
                {
                    return true;
                }

                try
                {
                    return string.Equals(
                        automation.FromHandle(handle).Properties.Name.Value,
                        "微信",
                        StringComparison.Ordinal);
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();
        if (titledHandles.Length > 0)
        {
            handles = titledHandles;
        }
        else if (handles.Count > 1)
        {
            automation.Dispose();
            throw new InvalidOperationException("检测到多个 Weixin 顶层窗口，但无法唯一确认标题为“微信”的主窗口。");
        }

        var candidates = new List<(Window Window, Rectangle Bounds)>();
        foreach (var handle in handles)
        {
            try
            {
                candidates.Add((automation.FromHandle(handle).AsWindow(), GetNativeBounds(handle)));
            }
            catch (InvalidOperationException)
            {
                // Ignore transient Weixin helper windows that disappear during selection.
            }
        }

        var orderedCandidates = candidates
            .OrderByDescending(candidate => candidate.Bounds.Width * candidate.Bounds.Height)
            .Select(candidate => candidate.Window)
            .ToArray();

        if (orderedCandidates.Length == 0)
        {
            automation.Dispose();
            var message = processIds.Count > 0
                ? "检测到 Weixin.exe，但微信窗口已最小化或隐藏。请恢复主窗口并保持可见。"
                : "没有检测到 Weixin.exe。请先打开并登录电脑版微信。";
            throw new InvalidOperationException(message);
        }

        if (orderedCandidates.Select(window => window.Properties.ProcessId.Value).Distinct().Count() > 1)
        {
            automation.Dispose();
            throw new InvalidOperationException("检测到多个可见微信窗口。为避免误发，请只保留一个微信窗口。");
        }

        return new VisualWeChatClient(automation, orderedCandidates[0], ocr);
    }

    public async Task<VisualCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var capture = CaptureWithLayout();
        using var bitmap = capture.Bitmap;
        var debugCapturePath = Environment.GetEnvironmentVariable("AICHAT_DEBUG_CAPTURE");
        if (!string.IsNullOrWhiteSpace(debugCapturePath))
        {
            bitmap.Save(Path.GetFullPath(debugCapturePath));
        }
        var layout = capture.Layout;
        var badges = RedBadgeDetector.Find(bitmap, layout.BadgeSearchArea)
            .Where(badge => RedBadgeDetector.IsPlausibleBadgeCenterX(badge.X, layout.SessionArea.Right))
            .ToArray();
        var mutedBadges = badges
            .Where(badge => MutedSessionDetector.IsMuted(bitmap, layout.SessionArea.Right, badge.Y))
            .ToArray();
        var result = await DetectAsync(bitmap, cancellationToken);
        try
        {
            return new VisualCheckResult(
                bitmap.Size,
                result.TextBlocks.Count(block => !string.IsNullOrWhiteSpace(block.Text)),
                badges,
                mutedBadges);
        }
        finally
        {
            result.BoxImg?.Dispose();
        }
    }

    public WeChatWindowProbe ProbeWindow()
    {
        var capture = CaptureWithLayout();
        using var bitmap = capture.Bitmap;
        return new WeChatWindowProbe(
            capture.Bounds,
            Rectangle.Round(_window.BoundingRectangle),
            capture.Layout.SessionArea.Left,
            capture.Layout.SessionArea.Right);
    }

    public async Task<IReadOnlyList<UnreadSession>> DetectUnreadSessionsAsync(CancellationToken cancellationToken)
    {
        var capture = CaptureWithLayout();
        using var bitmap = capture.Bitmap;
        var windowBounds = capture.Bounds;
        var layout = capture.Layout;
        var badges = RedBadgeDetector.Find(bitmap, layout.BadgeSearchArea)
            .Where(badge => RedBadgeDetector.IsPlausibleBadgeCenterX(badge.X, layout.SessionArea.Right))
            .ToArray();
        if (badges.Length == 0)
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
            if (IsDebugEnabled())
            {
                Console.WriteLine(
                    "会话列表 OCR：" + string.Join(
                        " | ",
                        blocks.Select(block => $"{block.Text}@({block.X},{block.Y})")));
            }
        }
        finally
        {
            ocr.BoxImg?.Dispose();
        }

        var sessions = new List<UnreadSession>();
        foreach (var badge in badges)
        {
            var isMuted = MutedSessionDetector.IsMuted(bitmap, layout.SessionArea.Right, badge.Y);
            var rowBlocks = blocks
                .Where(block => UnreadRowMatcher.IsInRow(block.Y, badge.Y, layout.RowHalfHeight))
                .Where(block => block.X < layout.SessionArea.Width * 0.82)
                .OrderBy(block => block.Y)
                .ThenBy(block => block.X)
                .ToArray();
            if (rowBlocks.Length == 0)
            {
                sessions.Add(new UnreadSession(
                    string.Empty,
                    string.Empty,
                    badge.Y,
                    isMuted,
                    windowBounds,
                    layout.SessionArea.Left,
                    layout.SessionArea.Right));
                continue;
            }

            var contact = rowBlocks[0].Text;
            var titleOffset = rowBlocks[0].Y - badge.Y;
            if (titleOffset is < 4 or > 24)
            {
                sessions.Add(new UnreadSession(
                    string.Empty,
                    string.Empty,
                    badge.Y,
                    isMuted,
                    windowBounds,
                    layout.SessionArea.Left,
                    layout.SessionArea.Right));
                continue;
            }

            var preview = string.Join(" ", rowBlocks.Skip(1).Select(block => block.Text)).Trim();
            if (!string.IsNullOrWhiteSpace(contact))
            {
                sessions.Add(new UnreadSession(
                    contact,
                    preview,
                    badge.Y,
                    isMuted,
                    windowBounds,
                    layout.SessionArea.Left,
                    layout.SessionArea.Right));
            }
        }

        return sessions.OrderBy(session => session.RowY).ToArray();
    }

    public async Task<string> OpenAndReadTitleAsync(UnreadSession session, CancellationToken cancellationToken)
    {
        if (session.IsMuted)
        {
            throw new InvalidOperationException("免打扰会话禁止点击。");
        }

        UnreadSession? fresh = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var freshMarkers = await DetectUnreadSessionsAsync(cancellationToken);
            fresh = freshMarkers
                .Where(marker => VisualMessagePolicy.SameContact(marker.Contact, session.Contact))
                .Where(marker => VisualMessagePolicy.Normalize(marker.Preview) == VisualMessagePolicy.Normalize(session.Preview))
                .Where(marker => !marker.IsMuted)
                .OrderBy(marker => Math.Abs(marker.RowY - session.RowY))
                .FirstOrDefault();
            if (fresh is null)
            {
                break;
            }

            _window.Focus();
            if (GetNativeBounds() == fresh.WindowBounds)
            {
                break;
            }

            fresh = null;
        }

        if (fresh is null)
        {
            throw new InvalidOperationException("点击前复核失败：窗口、未读红点、联系人或消息预览已经变化。");
        }

        var clickPoint = WindowInteractionGeometry.GetSessionClickPoint(
            fresh.WindowBounds,
            fresh.SessionLeft,
            fresh.SessionRight,
            fresh.RowY);
        if (IsDebugEnabled())
        {
            Console.WriteLine(
                $"点击诊断：截图窗口={fresh.WindowBounds}，输入窗口={_window.BoundingRectangle}，" +
                $"会话栏={fresh.SessionLeft}..{fresh.SessionRight}，" +
                $"行={fresh.RowY}，屏幕点击点={clickPoint}");
        }
        EnsureForeground();
        ClickPhysical(clickPoint);
        await Task.Delay(700, cancellationToken);

        var firstTitle = await ReadTitleAsync(cancellationToken);
        await Task.Delay(300, cancellationToken);
        var secondTitle = await ReadTitleAsync(cancellationToken);
        if (IsDebugEnabled())
        {
            Console.WriteLine($"点击后标题诊断：第一次={firstTitle}，第二次={secondTitle}");
        }
        if (!VisualMessagePolicy.SameConversationTitle(firstTitle, secondTitle))
        {
            throw new InvalidOperationException("点击后标题连续两次识别不一致，已取消回复。");
        }

        return secondTitle;
    }

    private async Task<string> ReadTitleAsync(CancellationToken cancellationToken)
    {
        var capture = CaptureWithLayout();
        using var bitmap = capture.Bitmap;
        var layout = capture.Layout;
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

    public async Task SendTextAsync(
        string text,
        string expectedTitle,
        bool sendWithCtrlEnter,
        CancellationToken cancellationToken)
    {
        _window.Focus();
        EnsureForeground();
        var firstTitle = await ReadTitleAsync(cancellationToken);
        await Task.Delay(250, cancellationToken);
        var secondTitle = await ReadTitleAsync(cancellationToken);
        if (!VisualMessagePolicy.SameConversationTitle(expectedTitle, firstTitle) ||
            !VisualMessagePolicy.SameConversationTitle(expectedTitle, secondTitle))
        {
            throw new InvalidOperationException("发送前标题复核失败，可能已切换会话；已取消发送。");
        }

        var snapshot = GetStableLayoutSnapshot();
        var inputPoint = WindowInteractionGeometry.GetMessageInputPoint(
            snapshot.Bounds,
            snapshot.Layout.SessionArea.Right);
        EnsureForeground();
        ClickPhysical(inputPoint);
        await Task.Delay(150, cancellationToken);

        // 微信会按会话保存输入框草稿；先清空，避免与历史草稿拼接后误发。
        ClearInputDraft();
        SetClipboardText(text);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        await Task.Delay(300, cancellationToken);

        EnsureForeground();
        var recognizedDraft = await ReadInputAreaTextAsync(cancellationToken);
        if (!ReplyDraftVerifier.ContainsReply(recognizedDraft, text))
        {
            ClearInputDraft();
            throw new InvalidOperationException("粘贴后未在输入框识别到回复内容，已清空草稿并取消发送。");
        }

        EnsureForeground();
        if (sendWithCtrlEnter)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.RETURN);
        }
        else
        {
            Keyboard.Type(VirtualKeyShort.RETURN);
        }
    }

    private static void ClearInputDraft()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(VirtualKeyShort.BACK);
        Thread.Sleep(80);
    }

    private async Task<string> ReadInputAreaTextAsync(CancellationToken cancellationToken)
    {
        var capture = CaptureWithLayout();
        using var bitmap = capture.Bitmap;
        var layout = capture.Layout;
        using var inputArea = bitmap.Clone(layout.InputArea, bitmap.PixelFormat);
        var result = await DetectAsync(inputArea, cancellationToken);
        try
        {
            return string.Join(
                " ",
                result.TextBlocks
                    .Where(block => !string.IsNullOrWhiteSpace(block.Text))
                    .OrderBy(block => CenterY(block))
                    .ThenBy(block => CenterX(block))
                    .Select(block => block.Text.Trim()));
        }
        finally
        {
            result.BoxImg?.Dispose();
        }
    }

    public async Task ScrollSessionListDownAsync(CancellationToken cancellationToken)
    {
        var snapshot = GetStableLayoutSnapshot();
        var sessionArea = snapshot.Layout.SessionArea;
        var point = new Point(
            snapshot.Bounds.Left + sessionArea.Left + sessionArea.Width / 2,
            snapshot.Bounds.Top + Math.Clamp(
                sessionArea.Top + (int)(sessionArea.Height * 0.70),
                sessionArea.Top,
                snapshot.Bounds.Height - 2));
        EnsureForeground();
        MovePointerPhysical(point);

        var wheel = new[]
        {
            new NativeInput
            {
                Type = InputMouse,
                Mouse = new NativeMouseInput
                {
                    MouseData = unchecked((uint)(-MouseWheelDelta * 5)),
                    Flags = MouseWheel
                }
            }
        };
        if (SendInput((uint)wheel.Length, wheel, Marshal.SizeOf<NativeInput>()) != wheel.Length)
        {
            throw new InvalidOperationException("会话列表滚动输入失败，已停止自动扫描。");
        }

        await Task.Delay(500, cancellationToken);
    }

    public void Dispose()
    {
        _automation.Dispose();
    }

    private (Rectangle Bounds, VisualLayout Layout) GetStableLayoutSnapshot()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var capture = CaptureWithLayout();
            using var bitmap = capture.Bitmap;
            _window.Focus();
            if (GetNativeBounds() == capture.Bounds)
            {
                return (capture.Bounds, capture.Layout);
            }
        }

        throw new InvalidOperationException("微信窗口在发送前持续移动或缩放，已取消发送。");
    }

    private static Rectangle GetVirtualScreenBounds() =>
        new(
            GetSystemMetrics(SystemMetricVirtualScreenX),
            GetSystemMetrics(SystemMetricVirtualScreenY),
            GetSystemMetrics(SystemMetricVirtualScreenWidth),
            GetSystemMetrics(SystemMetricVirtualScreenHeight));

    private Bitmap Capture(out Rectangle bounds)
    {
        _window.Focus();
        bounds = GetNativeBounds();
        if (bounds.Width < 300 || bounds.Height < 250)
        {
            throw new InvalidOperationException("微信窗口过小或已隐藏，请恢复窗口后重试。");
        }

        EnsureWindowFullyVisible(bounds);

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static void EnsureWindowFullyVisible(Rectangle bounds)
    {
        var screen = GetVirtualScreenBounds();
        const int tolerance = 8;
        var offBottom = bounds.Bottom - screen.Bottom;
        var offRight = bounds.Right - screen.Right;
        var offTop = screen.Top - bounds.Top;
        var offLeft = screen.Left - bounds.Left;
        if (offBottom <= tolerance && offRight <= tolerance &&
            offTop <= tolerance && offLeft <= tolerance)
        {
            return;
        }

        throw new InvalidOperationException(
            $"微信窗口有部分在屏幕外（窗口 {bounds}，虚拟桌面 {screen}），" +
            "屏幕外区域无法截取，点击点也可能落在屏外。请把微信主窗口完整拖回屏幕内后重试。");
    }

    private (Bitmap Bitmap, Rectangle Bounds, VisualLayout Layout) CaptureWithLayout()
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var bitmap = Capture(out var bounds);
            try
            {
                return (bitmap, bounds, GetLayout(bitmap));
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
                bitmap.Dispose();
                if (attempt < 2)
                {
                    Thread.Sleep(150);
                }
            }
        }

        throw new InvalidOperationException("连续多次无法识别微信界面布局（窗口可能正在重绘或被遮挡），已停止操作。", lastError);
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

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var title = new StringBuilder(length + 1);
        return GetWindowText(handle, title, title.Capacity) > 0 ? title.ToString() : string.Empty;
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
        if (!thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("写入剪贴板超时，剪贴板可能被其他程序占用。");
        }

        if (error is not null)
        {
            throw new InvalidOperationException("写入剪贴板失败。", error);
        }
    }

    private void EnsureForeground()
    {
        var handle = _window.Properties.NativeWindowHandle.Value;
        if (handle != IntPtr.Zero && GetForegroundWindow() == handle)
        {
            return;
        }

        _window.Focus();
        if (handle != IntPtr.Zero)
        {
            SetForegroundWindow(handle);
        }

        Thread.Sleep(150);
        if (handle == IntPtr.Zero || GetForegroundWindow() != handle)
        {
            throw new InvalidOperationException("微信窗口未处于前台，为避免点击或输入落入其他窗口，已取消本次操作。");
        }
    }

    private void ClickPhysical(Point physicalPoint)
    {
        MovePointerPhysical(physicalPoint);

        var down = new[]
        {
            new NativeInput
            {
                Type = InputMouse,
                Mouse = new NativeMouseInput { Flags = MouseLeftDown }
            }
        };
        if (SendInput((uint)down.Length, down, Marshal.SizeOf<NativeInput>()) != down.Length)
        {
            throw new InvalidOperationException("鼠标按下注入失败，已停止后续处理。");
        }

        // 按下与抬起之间保留短暂间隔，避免目标应用把零间隔点击吞掉。
        Thread.Sleep(45);

        var up = new[]
        {
            new NativeInput
            {
                Type = InputMouse,
                Mouse = new NativeMouseInput { Flags = MouseLeftUp }
            }
        };
        if (SendInput((uint)up.Length, up, Marshal.SizeOf<NativeInput>()) != up.Length)
        {
            throw new InvalidOperationException("鼠标抬起注入失败，已停止后续处理。");
        }
    }

    private static void MovePointerPhysical(Point physicalPoint)
    {
        var virtualScreen = GetVirtualScreenBounds();
        var normalized = WindowInteractionGeometry.NormalizeVirtualDesktopPoint(
            physicalPoint,
            virtualScreen);
        if (IsDebugEnabled())
        {
            Console.WriteLine(
                $"虚拟桌面点击诊断：桌面={virtualScreen}，物理点={physicalPoint}，绝对点={normalized}");
        }

        var move = new[]
        {
            new NativeInput
            {
                Type = InputMouse,
                Mouse = new NativeMouseInput
                {
                    X = normalized.X,
                    Y = normalized.Y,
                    Flags = MouseMove | MouseAbsolute | MouseVirtualDesktop
                }
            }
        };
        if (SendInput((uint)move.Length, move, Marshal.SizeOf<NativeInput>()) != move.Length)
        {
            throw new InvalidOperationException(
                $"无法将鼠标安全定位到动态目标 {physicalPoint}，已取消点击。");
        }

        // 移动注入后校验光标真实位置，防止在锁屏、远程桌面等场景下点击落空。
        Thread.Sleep(30);
        if (!GetCursorPos(out var cursor) ||
            Math.Abs(cursor.X - physicalPoint.X) > 3 ||
            Math.Abs(cursor.Y - physicalPoint.Y) > 3)
        {
            throw new InvalidOperationException(
                $"鼠标未能到达目标 {physicalPoint}（当前 {cursor.X},{cursor.Y}），已取消点击。");
        }
    }

    private static VisualLayout GetLayout(Bitmap bitmap)
    {
        var size = bitmap.Size;
        var sessionRight = WeChatLayoutDetector.FindSessionRight(bitmap);
        var sessionLeft = (int)(sessionRight * 0.26);
        var top = WindowInteractionGeometry.GetSessionSearchTop(size.Height);
        var badgeLeft = Math.Clamp((int)(sessionRight * 0.40), sessionLeft + 18, sessionRight - 16);
        var badgeRight = Math.Min(sessionRight - 8, (int)(sessionRight * 0.52));
        var inputTop = Math.Max((int)(size.Height * 0.70), size.Height - 320);
        return new VisualLayout(
            new Rectangle(sessionLeft, top, sessionRight - sessionLeft, size.Height - top),
            new Rectangle(badgeLeft, top, badgeRight - badgeLeft, size.Height - top),
            new Rectangle(
                sessionRight,
                0,
                size.Width - sessionRight,
                Math.Min(size.Height, Math.Max(105, top + 35))),
            Math.Max(22, (int)(size.Height * 0.065)),
            new Rectangle(
                sessionRight + 4,
                inputTop,
                Math.Max(16, size.Width - sessionRight - 8),
                Math.Max(16, size.Height - inputTop - 4)));
    }

    private static int CenterX(TextBlock block) => (block.BoxPoints[0].X + block.BoxPoints[2].X) / 2;
    private static int CenterY(TextBlock block) => (block.BoxPoints[0].Y + block.BoxPoints[2].Y) / 2;

    private static bool IsDebugEnabled() => string.Equals(
        Environment.GetEnvironmentVariable("AICHAT_DEBUG"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    private sealed record TextBox(string Text, int X, int Y);
    private sealed record VisualLayout(
        Rectangle SessionArea,
        Rectangle BadgeSearchArea,
        Rectangle HeaderArea,
        int RowHalfHeight,
        Rectangle InputArea);
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
    private struct NativeInput
    {
        public uint Type;
        public NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder title, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr windowHandle, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, ShowWindowCommand command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        NativeInput[] inputs,
        int inputSize);

    private const uint InputMouse = 0;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseWheel = 0x0800;
    private const uint MouseVirtualDesktop = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const int SystemMetricVirtualScreenX = 76;
    private const int SystemMetricVirtualScreenY = 77;
    private const int SystemMetricVirtualScreenWidth = 78;
    private const int SystemMetricVirtualScreenHeight = 79;
    private const int MouseWheelDelta = 120;
}
