using System.Collections.Concurrent;

namespace WeChatOllamaAutoReply;

public sealed class VisualAutoReplyService(
    VisualWeChatClient wechat,
    OllamaChatClient ollama,
    AppOptions options,
    CancellationToken applicationToken)
{
    private readonly ConcurrentDictionary<string, List<MessageSnapshot>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly DuplicateGuard _duplicates = new(TimeSpan.FromMinutes(10));

    public async Task RunAsync()
    {
        var baseline = (await wechat.DetectUnreadSessionsAsync(applicationToken))
            .Select(session => session.Key)
            .ToHashSet(StringComparer.Ordinal);
        Console.WriteLine($"已建立未读基线（{baseline.Count} 项）；启动前已有未读不会自动回复。");

        while (!applicationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), applicationToken);
            IReadOnlyList<UnreadSession> current;
            try
            {
                current = await wechat.DetectUnreadSessionsAsync(applicationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] 微信视觉检测失败：{ex.Message}");
                continue;
            }

            var currentKeys = current.Select(session => session.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var session in current.Where(session => !baseline.Contains(session.Key)))
            {
                await ProcessAsync(session);
            }

            baseline = currentKeys;
        }
    }

    private async Task ProcessAsync(UnreadSession session)
    {
        if (!VisualMessagePolicy.IsPlainTextPreview(session.Preview))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已忽略 {session.Contact} 的非文字预览。");
            return;
        }

        var snapshot = new MessageSnapshot(session.Contact, session.Preview, true, DateTime.Now);
        if (!_duplicates.TryAccept(session.Contact, [snapshot], DateTimeOffset.Now))
        {
            return;
        }

        try
        {
            var title = await wechat.OpenAndReadTitleAsync(session, applicationToken);
            if (!VisualMessagePolicy.IsLikelyPrivateChat(title, options.AllowedContacts))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已忽略非私聊会话：{title}");
                return;
            }

            if (!VisualMessagePolicy.NamesMatch(session.Contact, title))
            {
                Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] 会话标题校验失败，未回复：列表={session.Contact}，标题={title}");
                return;
            }

            var history = _history.GetOrAdd(title, _ => []);
            IReadOnlyList<OllamaMessage> prompt;
            lock (history)
            {
                prompt = ConversationComposer.Build(options.SystemPrompt, history, [snapshot], options.MaxHistoryMessages);
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 收到 {title} 的文字消息，正在调用 {options.Model}…");
            var reply = await ollama.ReplyAsync(prompt, applicationToken);
            if (options.DryRun)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [dry-run] 已生成 {reply.Length} 字回复，未发送。");
                return;
            }

            wechat.SendText(reply, options.SendWithCtrlEnter);
            lock (history)
            {
                history.Add(snapshot);
                history.Add(new MessageSnapshot("我", reply, true, DateTime.Now));
                if (history.Count > options.MaxHistoryMessages * 2)
                {
                    history.RemoveRange(0, history.Count - options.MaxHistoryMessages);
                }
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已向 {title} 发送 {reply.Length} 字回复。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] 本次消息处理失败，未发送回复：{ex.Message}");
        }
    }
}
