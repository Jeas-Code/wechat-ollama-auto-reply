namespace WeChatOllamaAutoReply;

public sealed record AppOptions
{
    public const string DefaultSystemPrompt = """
        你是用户的微信私聊助手。只输出要发给对方的中文回复正文，不解释你的工作过程。
        回复要自然、礼貌、简洁，通常不超过三句话；不确定的信息要坦诚说明。
        你不能代表用户确认转账、借款、合同、验证码、账号密码、法律或医疗决定，也不能擅自承诺线下见面时间。
        遇到这些高影响事项时，只回复“我先确认一下，稍后回复你”。不要声称已经执行任何现实世界操作。
        """;

    public Uri OllamaBaseUri { get; init; } = new("http://127.0.0.1:11434/");
    public string Model { get; init; } = "gemma4:12b";
    public string SystemPrompt { get; init; } = DefaultSystemPrompt;
    public string OcrModelsDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "models");
    public int MaxHistoryMessages { get; init; } = 16;
    public int MaxReplyCharacters { get; init; } = 500;
    public int OllamaTimeoutSeconds { get; init; } = 120;
    public int PollIntervalSeconds { get; init; } = 3;
    public IReadOnlySet<string> AllowedContacts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool SendWithCtrlEnter { get; init; }
    public bool DryRun { get; init; }
    public bool CheckOnly { get; init; }
    public bool ShowHelp { get; init; }

    public static AppOptions Load(string[] args)
    {
        var options = new AppOptions
        {
            OllamaBaseUri = ReadUri("AICHAT_OLLAMA_URL", "http://127.0.0.1:11434/"),
            Model = ReadString("AICHAT_MODEL", "gemma4:12b"),
            SystemPrompt = ReadString("AICHAT_SYSTEM_PROMPT", DefaultSystemPrompt),
            OcrModelsDirectory = Path.GetFullPath(ReadString(
                "AICHAT_OCR_MODELS_DIR",
                Path.Combine(AppContext.BaseDirectory, "models"))),
            MaxHistoryMessages = ReadInt("AICHAT_MAX_HISTORY", 16, 0, 100),
            MaxReplyCharacters = ReadInt("AICHAT_MAX_REPLY_CHARS", 500, 20, 4000),
            OllamaTimeoutSeconds = ReadInt("AICHAT_OLLAMA_TIMEOUT_SECONDS", 120, 10, 600),
            PollIntervalSeconds = ReadInt("AICHAT_POLL_SECONDS", 3, 1, 30),
            AllowedContacts = ReadSet("AICHAT_ALLOWED_CONTACTS"),
            SendWithCtrlEnter = ReadSendWithCtrlEnter(),
            DryRun = ReadBool("AICHAT_DRY_RUN", false) || args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase),
            CheckOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase),
            ShowHelp = args.Any(arg => arg is "--help" or "-h")
        };

        var unknownArgs = args.Where(arg => arg is not "--dry-run" and not "--check" and not "--help" and not "-h").ToArray();
        if (unknownArgs.Length > 0)
        {
            throw new ArgumentException($"未知参数：{string.Join(", ", unknownArgs)}");
        }

        return options;
    }

    public IReadOnlyDictionary<string, string> GetRequiredOcrModelPaths()
    {
        var fileNames = new[]
        {
            "ch_PP-OCRv5_mobile_det.onnx",
            "ch_ppocr_mobile_v2.0_cls_infer.onnx",
            "ch_PP-OCRv5_rec_mobile_infer.onnx",
            "ppocrv5_dict.txt"
        };

        return fileNames.ToDictionary(name => name, name => Path.Combine(OcrModelsDirectory, name));
    }

    public void EnsureOcrModelsExist()
    {
        var missing = GetRequiredOcrModelPaths().Values.Where(path => !File.Exists(path)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        throw new FileNotFoundException(
            "缺少微信 OCR 模型：" + string.Join(", ", missing.Select(Path.GetFileName)) +
            $"。请按 models/README.md 下载，或设置 AICHAT_OCR_MODELS_DIR。当前目录：{OcrModelsDirectory}");
    }

    private static string ReadString(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? fallback
            : Environment.GetEnvironmentVariable(name)!.Trim();

    private static Uri ReadUri(string name, string fallback)
    {
        var text = ReadString(name, fallback);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"环境变量 {name} 必须是 http(s) URL，当前值：{text}");
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + '/');
    }

    private static int ReadInt(string name, int fallback, int minimum, int maximum)
    {
        var text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (!int.TryParse(text, out var value) || value < minimum || value > maximum)
        {
            throw new ArgumentException($"环境变量 {name} 必须是 {minimum} 到 {maximum} 之间的整数。");
        }

        return value;
    }

    private static bool ReadBool(string name, bool fallback)
    {
        var text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (!bool.TryParse(text, out var value))
        {
            throw new ArgumentException($"环境变量 {name} 必须是 true 或 false。");
        }

        return value;
    }

    private static IReadOnlySet<string> ReadSet(string name) =>
        new HashSet<string>(
            (Environment.GetEnvironmentVariable(name) ?? string.Empty)
                .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    private static bool ReadSendWithCtrlEnter()
    {
        var value = ReadString("AICHAT_SEND_HOTKEY", "ENTER");
        return value.ToUpperInvariant() switch
        {
            "ENTER" => false,
            "CTRL_ENTER" => true,
            _ => throw new ArgumentException("环境变量 AICHAT_SEND_HOTKEY 必须是 ENTER 或 CTRL_ENTER。")
        };
    }
}
