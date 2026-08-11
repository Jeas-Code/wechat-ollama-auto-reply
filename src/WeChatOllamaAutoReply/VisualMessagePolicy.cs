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

    public static bool IsSupportedConversation(
        string title,
        IReadOnlySet<string> allowedContacts,
        bool allowAllUnmutedChats)
    {
        var normalized = Normalize(title);
        if (normalized.Length == 0 || SystemChats.Contains(normalized))
        {
            return false;
        }

        return allowAllUnmutedChats ||
               allowedContacts.Any(contact => ConversationMatchesList(contact, normalized));
    }

    public static bool IsGroupChat(string title) => GroupCountSuffix().IsMatch(Normalize(title));

    public static bool IsSystemChat(string title) => SystemChats.Contains(Normalize(title));

    public static bool ConversationMatchesList(string listTitle, string openedTitle)
    {
        if (SameContact(listTitle, openedTitle))
        {
            return true;
        }

        if (!IsGroupChat(openedTitle))
        {
            return false;
        }

        var listKey = ContactKey(listTitle);
        var groupKey = ContactKey(RemoveGroupCount(openedTitle));
        return listKey.Length >= 4 && groupKey.Length >= 4 &&
               (listKey.StartsWith(groupKey, StringComparison.Ordinal) ||
                groupKey.StartsWith(listKey, StringComparison.Ordinal));
    }

    public static bool SameConversationTitle(string first, string second)
    {
        if (SameContact(first, second))
        {
            return true;
        }

        if (!IsGroupChat(first) || !IsGroupChat(second))
        {
            return false;
        }

        return SameContact(RemoveGroupCount(first), RemoveGroupCount(second));
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

    public static bool SameContact(string expected, string actual)
    {
        var left = ContactKey(expected);
        var right = ContactKey(actual);
        return left.Length >= 2 && string.Equals(left, right, StringComparison.Ordinal);
    }

    public static string Normalize(string value) =>
        Regex.Replace(value ?? string.Empty, @"[\s\u200B\uFEFF]", string.Empty).Trim('"', '\'', '“', '”');

    public static string ContactKey(string value) =>
        Regex.Replace(Normalize(value), @"[^\p{L}\p{N}]", string.Empty).ToUpperInvariant();

    private static string RemoveGroupCount(string value) =>
        GroupCountSuffix().Replace(Normalize(value), string.Empty);

    [GeneratedRegex(@"[（(]\s*\d+\s*[）)]$")]
    private static partial Regex GroupCountSuffix();

    [GeneratedRegex(@"^\s*[\[【].+[\]】]\s*$")]
    private static partial Regex BracketedMessage();
}
