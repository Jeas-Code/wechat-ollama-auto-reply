using System.Text.RegularExpressions;

namespace WeChatOllamaAutoReply;

public static partial class VisualMessagePolicy
{
    private static readonly HashSet<string> SystemChats = new(StringComparer.OrdinalIgnoreCase)
    {
        "公众号", "服务号", "订阅号", "订阅号消息", "服务通知", "微信支付", "微信团队",
        "腾讯新闻", "文件传输助手", "企业微信联系人", "群聊", "折叠的群聊"
    };

    public static bool IsLikelyPrivateChat(string title, IReadOnlySet<string> allowedContacts)
    {
        var normalized = Normalize(title);
        if (normalized.Length == 0 || SystemChats.Contains(normalized) || GroupCountSuffix().IsMatch(normalized))
        {
            return false;
        }

        return allowedContacts.Count == 0 || allowedContacts.Any(contact => NamesMatch(contact, normalized));
    }

    public static bool IsPlainTextPreview(string preview)
    {
        var text = preview.Trim();
        if (text.Length == 0 || BracketedMessage().IsMatch(text) || text.StartsWith("[有人@我]", StringComparison.Ordinal))
        {
            return false;
        }

        return !text.StartsWith("以下为新消息", StringComparison.Ordinal) &&
               !text.Contains("撤回了一条消息", StringComparison.Ordinal);
    }

    public static bool NamesMatch(string expected, string actual)
    {
        var left = Normalize(expected);
        var right = Normalize(actual);
        return left.Length > 0 && right.Length > 0 &&
               (string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
                left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
                right.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string value) =>
        Regex.Replace(value ?? string.Empty, @"[\s\u200B\uFEFF]", string.Empty).Trim('"', '\'', '“', '”');

    public static string ContactKey(string value) =>
        Regex.Replace(Normalize(value), @"[^\p{L}\p{N}]", string.Empty).ToUpperInvariant();

    [GeneratedRegex(@"[（(]\s*\d+\s*[）)]$")]
    private static partial Regex GroupCountSuffix();

    [GeneratedRegex(@"^\s*[\[【].+[\]】]\s*$")]
    private static partial Regex BracketedMessage();
}
