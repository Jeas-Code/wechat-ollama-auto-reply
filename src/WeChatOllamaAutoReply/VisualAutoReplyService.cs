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
        if (!options.DryRun && options.AllowedContacts.Count == 0 && !options.AllowAllUnmutedChats)
        {
            throw new InvalidOperationException(
                "安全模式要求设置 AICHAT_ALLOWED_CONTACTS，或显式启用 AICHAT_ALLOW_ALL_UNMUTED_CHATS=true。");
        }

        var baseline = await wechat.DetectUnreadSessionsAsync(applicationToken);
        var gate = new UnreadClickGate();
        gate.Initialize(baseline);
        var consecutiveDetectionFailures = 0;
        Console.WriteLine($"已建立未读红点基线（{baseline.Count} 项）；启动前已有红点不会被点击。");

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
                consecutiveDetectionFailures++;
                Console.Error.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss}] 微信视觉检测失败 " +
                    $"({consecutiveDetectionFailures}/3)：{ex.Message}");
                if (consecutiveDetectionFailures >= 3)
                {
                    Console.Error.WriteLine("连续检测失败，服务已安全停止；请修复窗口状态后手动重启。");
                    return;
                }

                continue;
            }

            consecutiveDetectionFailures = 0;
            var ready = gate.Observe(current);

            foreach (var session in ready)
            {
                if (options.DryRun)
                {
                    Console.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss}] [dry-run] 稳定检测到新红点：" +
                        $"联系人={session.Contact}，行={session.RowY}；零点击、零发送。");
                    continue;
                }

                await ProcessAsync(session);
            }
        }
    }

    private async Task ProcessAsync(UnreadSession session)
    {
        if (session.IsMuted)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已忽略免打扰会话：{session.Contact}");
            return;
        }

        if (VisualMessagePolicy.IsSystemChat(session.Contact))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已忽略系统会话：{session.Contact}");
            return;
        }

        if (!options.AllowAllUnmutedChats &&
            !options.AllowedContacts.Any(contact =>
                VisualMessagePolicy.ConversationMatchesList(contact, session.Contact)))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已忽略白名单外联系人：{session.Contact}");
            return;
        }

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
            if (!VisualMessagePolicy.IsSupportedConversation(
                    title,
                    options.AllowedContacts,
                    options.AllowAllUnmutedChats))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已忽略不支持的会话：{title}");
                return;
            }

            if (!VisualMessagePolicy.ConversationMatchesList(session.Contact, title) ||
                (!options.AllowAllUnmutedChats &&
                 !options.AllowedContacts.Any(contact =>
                     VisualMessagePolicy.ConversationMatchesList(contact, title))))
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
            await wechat.SendTextAsync(reply, title, options.SendWithCtrlEnter, applicationToken);
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
