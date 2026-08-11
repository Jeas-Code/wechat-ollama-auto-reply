# 微信 Ollama AI 自动回复

在 Windows 电脑版微信中监听**好友私聊文字消息**，调用本机 Ollama 的
`gemma4:12b` 生成回复并自动发送。消息与上下文只提交到默认的本地地址
`127.0.0.1:11434`，不接入云端聊天服务。

> 本项目通过窗口截图、颜色检测、OCR 与桌面输入工作，不修改微信、不注入进程，也不绕过平台风控。
> 微信更新可能导致自动化失效；请先用 `--check` 和 `--dry-run` 验证，并自行遵守微信
> 服务条款。不要将它用于骚扰、群发或高频营销。

## 功能边界

- 点击未读会话后会再次 OCR 标题；群聊人数标题、公众号、服务通知等会话全部忽略。
- 只处理对方发来的文字；本人消息、系统消息、图片、语音、文件等全部忽略。
- 不点击红包或转账，不读取图片或语音，不自动接受好友请求。
- 默认提示词禁止替用户承诺转账、借款、合同、验证码及其他高影响事项。
- 支持本次运行内的聊天上下文、重复消息保护、Ollama 短暂故障重试。
- 启动时先记录现有未读基线，只处理启动后新出现的未读，避免误回历史消息。
- `--check` 不监听不发送；`--dry-run` 监听并生成，但不发送。

## 环境要求

- Windows 10/11，电脑版微信 4.x 已登录，主窗口保持可见且桌面会话解锁。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
- [Ollama](https://ollama.com/) 已运行，且本地存在 `gemma4:12b`：

```powershell
ollama pull gemma4:12b
ollama list
```

## 安装

1. 克隆仓库并还原依赖：

```powershell
git clone https://github.com/Jeas-Code/wechat-ollama-auto-reply.git
cd wechat-ollama-auto-reply
dotnet restore AIChat.slnx
```

2. 按 [models/README.md](models/README.md) 下载四个 OCR 文件，放入根目录的
   `models` 文件夹。模型二进制不会提交到本仓库。

3. 先执行无副作用自检：

```powershell
dotnet run --project src/WeChatOllamaAutoReply -- --check
```

4. 推荐先试运行，确认只捕获预期私聊：

```powershell
dotnet run --project src/WeChatOllamaAutoReply -- --dry-run
```

5. 确认无误后启动自动回复；按 `Ctrl+C` 停止：

```powershell
dotnet run --project src/WeChatOllamaAutoReply
```

监听过程中程序会截图、OCR、切换微信会话并模拟桌面输入。请不要最小化或遮挡微信，
不要同时操作微信窗口，也不要同时启动多个实例。检测到多个可见微信窗口时程序会安全
退出，避免把回复发送到错误账号。

## 配置

全部配置通过环境变量传入，不需要 `.env`，也不要把凭据写进仓库。

| 环境变量 | 默认值 | 说明 |
| --- | --- | --- |
| `AICHAT_OLLAMA_URL` | `http://127.0.0.1:11434/` | Ollama API 根地址 |
| `AICHAT_MODEL` | `gemma4:12b` | 本地模型名 |
| `AICHAT_SYSTEM_PROMPT` | 内置安全中文提示词 | 回复风格与边界 |
| `AICHAT_OCR_MODELS_DIR` | 输出目录下的 `models` | 四个 OCR 文件所在目录 |
| `AICHAT_MAX_HISTORY` | `16` | 发送给模型的最大历史消息数，0–100 |
| `AICHAT_MAX_REPLY_CHARS` | `500` | 回复最大字符数，20–4000 |
| `AICHAT_OLLAMA_TIMEOUT_SECONDS` | `120` | Ollama 超时秒数，10–600 |
| `AICHAT_POLL_SECONDS` | `3` | 微信截图轮询间隔，1–30 秒 |
| `AICHAT_ALLOWED_CONTACTS` | 空 | 可选联系人白名单，逗号分隔；空表示允许所有疑似好友私聊 |
| `AICHAT_SEND_HOTKEY` | `ENTER` | 微信发送快捷键：`ENTER` 或 `CTRL_ENTER` |
| `AICHAT_DRY_RUN` | `false` | `true` 时生成但不发送 |

PowerShell 示例：

```powershell
$env:AICHAT_SYSTEM_PROMPT = '你是我的微信助理。回复友好、简短，不做任何付款或承诺。'
$env:AICHAT_MAX_HISTORY = '10'
$env:AICHAT_ALLOWED_CONTACTS = '张三,李四'
dotnet run --project src/WeChatOllamaAutoReply -- --dry-run
```

## 开发与测试

```powershell
dotnet build AIChat.slnx --configuration Release
dotnet test AIChat.slnx --configuration Release --no-build
```

核心分层：

- `Program.cs`：启动、自检和视觉监听。
- `VisualWeChatClient.cs`：窗口截图、OCR、会话点击与文本发送。
- `VisualAutoReplyService.cs`：未读基线、私聊校验和回复闭环。
- `RedBadgeDetector.cs`：从会话列表识别红色未读标记。
- `VisualMessagePolicy.cs`：群聊/系统会话与非文字预览拦截。
- `ConversationComposer.cs`：可测试的角色映射、历史截断与去重。
- `OllamaChatClient.cs`：Ollama `/api/tags`、`/api/chat` 调用及错误处理。

## 已知限制

- 微信不是为个人账号机器人开放的官方接口，本项目依赖桌面视觉布局；升级微信后请重新
  运行 `--check`。
- OCR 和红点检测是启发式方案，强烈建议先配置 `AICHAT_ALLOWED_CONTACTS` 并使用
  `--dry-run` 观察。
- 当前只支持文字私聊，不支持图片理解、语音、引用、群聊或朋友圈。
- 自动化和模型都可能犯错。涉及资金、合同、隐私、法律、医疗或账号安全时必须人工确认。

## 致谢与许可

窗口自动化基于 MIT 许可的 [FlaUI](https://github.com/FlaUI/FlaUI)，本地 OCR 使用
[RapidOCRLib](https://github.com/scottfly189/RapidOCRLib)，模型推理由本地
[Ollama](https://github.com/ollama/ollama) 提供。第三方说明见
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本项目使用 [MIT License](LICENSE)。
