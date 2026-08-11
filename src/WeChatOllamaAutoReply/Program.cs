namespace WeChatOllamaAutoReply;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            var options = AppOptions.Load(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            using var httpClient = new HttpClient
            {
                BaseAddress = options.OllamaBaseUri,
                Timeout = TimeSpan.FromSeconds(options.OllamaTimeoutSeconds)
            };
            var ollama = new OllamaChatClient(httpClient, options);

            Console.WriteLine($"正在检查本地 Ollama 模型 {options.Model}…");
            await ollama.EnsureModelAvailableAsync(cancellation.Token);
            Console.WriteLine("Ollama 模型检查通过。");

            options.EnsureOcrModelsExist();
            Console.WriteLine("正在初始化本地 OCR 并连接可见微信窗口…");
            using var wechat = await VisualWeChatClient.CreateAsync(options, cancellation.Token);

            if (options.CheckOnly)
            {
                var check = await wechat.CheckAsync(cancellation.Token);
                var unread = await wechat.DetectUnreadSessionsAsync(cancellation.Token);
                var badgeText = check.BadgeCandidates.Count == 0
                    ? "无"
                    : string.Join(", ", check.BadgeCandidates.Select(point => $"({point.X},{point.Y})"));
                var mutedText = check.MutedBadgeCandidates.Count == 0
                    ? "无"
                    : string.Join(", ", check.MutedBadgeCandidates.Select(point => $"({point.X},{point.Y})"));
                Console.WriteLine(
                    $"自检完成：窗口 {check.WindowSize.Width}x{check.WindowSize.Height}，" +
                    $"OCR {check.TextBlockCount} 个文本块，红点候选 {badgeText}，" +
                    $"其中免打扰 {mutedText}，映射未读 {unread.Count} 项；未监听、未发送。");
                return 0;
            }

            if (!options.DryRun && options.AllowedContacts.Count == 0 && !options.AllowAllUnmutedChats)
            {
                throw new InvalidOperationException(
                    "安全模式要求设置 AICHAT_ALLOWED_CONTACTS，或显式启用 AICHAT_ALLOW_ALL_UNMUTED_CHATS=true。");
            }

            Console.WriteLine(options.DryRun
                ? "个人与非免打扰群聊监听已启动（dry-run：零点击、零发送）。按 Ctrl+C 停止。"
                : "个人与非免打扰群聊自动回复已启动。按 Ctrl+C 停止。");

            try
            {
                await new VisualAutoReplyService(wechat, ollama, options, cancellation.Token).RunAsync();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Console.WriteLine("正在停止监听…");
            }

            return 0;
        }
        catch (Exception ex)
        {
            var debug = string.Equals(
                Environment.GetEnvironmentVariable("AICHAT_DEBUG"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            Console.Error.WriteLine(debug ? ex.ToString() : $"启动失败：{ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            微信 Ollama 自动回复

            用法：
              dotnet run --project src/WeChatOllamaAutoReply -- [--check] [--dry-run]

            参数：
              --check     只检查 Ollama、OCR 模型和微信窗口，不监听、不发送
              --dry-run   只验证新红点稳定性，零点击、零发送
              --help, -h  显示帮助

            配置请参阅 README.md 中的环境变量表。
            """);
    }
}
