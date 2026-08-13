namespace WeChatOllamaAutoReply;

public static class UnreadRowMatcher
{
    public static bool IsInRow(int blockY, int badgeY, int rowHalfHeight)
    {
        var offset = blockY - badgeY;
        return offset is >= 4 && offset <= rowHalfHeight;
    }
}
