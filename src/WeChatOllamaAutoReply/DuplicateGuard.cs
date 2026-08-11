using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace WeChatOllamaAutoReply;

public sealed class DuplicateGuard(TimeSpan retention)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);

    public bool TryAccept(string conversation, IEnumerable<MessageSnapshot> messages, DateTimeOffset now)
    {
        var material = string.Join(
            "\n",
            messages.Select(message => $"{conversation}|{message.Sender}|{message.SentAt:O}|{message.Text}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));

        foreach (var entry in _seen.Where(entry => now - entry.Value > retention))
        {
            _seen.TryRemove(entry.Key, out _);
        }

        return _seen.TryAdd(fingerprint, now);
    }
}
