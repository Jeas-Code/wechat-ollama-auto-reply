namespace WeChatOllamaAutoReply;

public sealed record MessageSnapshot(string Sender, string Text, bool IsText, DateTime SentAt);

public static class ConversationComposer
{
    private const string SelfSender = "我";
    private const string SystemSender = "系统";

    public static IReadOnlyList<MessageSnapshot> IncomingTextMessages(IEnumerable<MessageSnapshot> messages) =>
        messages
            .Where(message => message.IsText)
            .Where(message => !string.IsNullOrWhiteSpace(message.Text))
            .Where(message => !IsSelfOrSystem(message.Sender))
            .ToArray();

    public static IReadOnlyList<OllamaMessage> Build(
        string systemPrompt,
        IEnumerable<MessageSnapshot> history,
        IEnumerable<MessageSnapshot> incoming,
        int maxHistoryMessages)
    {
        var historyMessages = history
            .Where(message => message.IsText && !string.IsNullOrWhiteSpace(message.Text))
            .Where(message => !string.Equals(message.Sender, SystemSender, StringComparison.Ordinal))
            .Select(ToOllamaMessage)
            .TakeLast(maxHistoryMessages)
            .ToList();

        var incomingMessages = IncomingTextMessages(incoming)
            .Select(message => new OllamaMessage("user", message.Text.Trim()))
            .ToList();

        if (!EndsWith(historyMessages, incomingMessages))
        {
            historyMessages.AddRange(incomingMessages);
        }

        return [new OllamaMessage("system", systemPrompt.Trim()), .. historyMessages];
    }

    private static OllamaMessage ToOllamaMessage(MessageSnapshot message) =>
        new(IsSelfOrSystem(message.Sender) ? "assistant" : "user", message.Text.Trim());

    private static bool IsSelfOrSystem(string? sender) =>
        string.Equals(sender, SelfSender, StringComparison.Ordinal) ||
        string.Equals(sender, SystemSender, StringComparison.Ordinal);

    private static bool EndsWith(IReadOnlyList<OllamaMessage> source, IReadOnlyList<OllamaMessage> suffix)
    {
        if (suffix.Count == 0 || suffix.Count > source.Count)
        {
            return suffix.Count == 0;
        }

        var offset = source.Count - suffix.Count;
        return suffix.Select((message, index) => message == source[offset + index]).All(equal => equal);
    }
}
