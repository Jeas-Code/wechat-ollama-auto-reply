using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WeChatOllamaAutoReply;

public sealed record OllamaMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed class OllamaChatClient(HttpClient httpClient, AppOptions options)
{
    private static readonly Regex ThinkBlock = new("<think>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public async Task EnsureModelAvailableAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("api/tags", cancellationToken);
        await EnsureSuccessAsync(response, "读取 Ollama 模型列表");
        var payload = await response.Content.ReadFromJsonAsync<TagsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ollama /api/tags 返回了空响应。");

        if (!payload.Models.Any(model => string.Equals(model.Name, options.Model, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"本地 Ollama 中没有模型 {options.Model}。请先运行：ollama pull {options.Model}");
        }
    }

    public async Task<string> ReplyAsync(IReadOnlyList<OllamaMessage> messages, CancellationToken cancellationToken)
    {
        var request = new ChatRequest(
            options.Model,
            messages,
            Stream: false,
            Think: false,
            new ChatOptions(Temperature: 0.7, NumPredict: 512));

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await httpClient.PostAsJsonAsync("api/chat", request, cancellationToken);
                await EnsureSuccessAsync(response, "调用 Ollama 对话接口");
                var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException("Ollama /api/chat 返回了空响应。");
                var reply = CleanReply(payload.Message?.Content);
                if (reply.Length == 0)
                {
                    throw new InvalidOperationException("Ollama 没有生成可发送的回复。");
                }

                return reply;
            }
            catch (Exception ex) when (attempt < 2 && ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken);
            }
        }

        throw new InvalidOperationException("Ollama 调用失败。", lastError);
    }

    private string CleanReply(string? content)
    {
        var reply = ThinkBlock.Replace(content ?? string.Empty, string.Empty).Trim();
        if (reply.Length > options.MaxReplyCharacters)
        {
            reply = reply[..options.MaxReplyCharacters].TrimEnd() + "…";
        }

        return reply;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "接口或模型不存在",
            HttpStatusCode.ServiceUnavailable => "Ollama 服务不可用",
            _ => "HTTP 请求失败"
        };
        await response.Content.LoadIntoBufferAsync();
        throw new HttpRequestException($"{operation}失败：{(int)response.StatusCode} {detail}。", null, response.StatusCode);
    }

    private sealed record TagsResponse([property: JsonPropertyName("models")] IReadOnlyList<ModelInfo> Models);
    private sealed record ModelInfo([property: JsonPropertyName("name")] string Name);
    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OllamaMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("think")] bool Think,
        [property: JsonPropertyName("options")] ChatOptions Options);
    private sealed record ChatOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict);
    private sealed record ChatResponse([property: JsonPropertyName("message")] OllamaMessage? Message);
}
