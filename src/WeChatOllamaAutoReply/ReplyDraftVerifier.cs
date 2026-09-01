namespace WeChatOllamaAutoReply;

/// <summary>
/// 粘贴后、按下回车前，对输入框 OCR 文本做存在性校验：只有确认回复内容确实进入
/// 输入框才允许发送，任何不确定都按失败处理（fail closed）。
/// </summary>
public static class ReplyDraftVerifier
{
    private const int FragmentLength = 8;

    public static bool ContainsReply(string recognizedText, string reply)
    {
        var strictHaystack = VisualMessagePolicy.Normalize(recognizedText);
        var strictNeedle = VisualMessagePolicy.Normalize(reply);
        if (strictNeedle.Length == 0 || strictHaystack.Length == 0)
        {
            return false;
        }

        if (strictHaystack.Contains(strictNeedle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // OCR 对标点、全半角容易误判，退化为只保留字母与数字的宽松比较。
        var looseHaystack = VisualMessagePolicy.ContactKey(recognizedText);
        var looseNeedle = VisualMessagePolicy.ContactKey(reply);
        if (looseNeedle.Length == 0)
        {
            return false;
        }

        if (looseHaystack.Contains(looseNeedle, StringComparison.Ordinal))
        {
            return true;
        }

        if (looseNeedle.Length <= FragmentLength)
        {
            return false;
        }

        // 长回复允许 OCR 局部出错：头部、中部、尾部任一片段完整命中即视为粘贴成功。
        return LooseFragments(looseNeedle).Any(fragment =>
            looseHaystack.Contains(fragment, StringComparison.Ordinal));
    }

    private static IEnumerable<string> LooseFragments(string looseNeedle)
    {
        yield return looseNeedle[..FragmentLength];
        yield return looseNeedle.Substring((looseNeedle.Length - FragmentLength) / 2, FragmentLength);
        yield return looseNeedle[^FragmentLength..];
    }
}
