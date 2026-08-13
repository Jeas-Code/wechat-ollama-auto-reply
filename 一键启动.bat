@echo off
setlocal
title WeChat Ollama AI Auto Reply

set "ROOT=%~dp0"
set "DOTNET_EXE=%ROOT%.dotnet\dotnet.exe"
set "PROJECT=%ROOT%src\WeChatOllamaAutoReply\WeChatOllamaAutoReply.csproj"
set "PROJECT_MODELS=%ROOT%models"
set "FALLBACK_MODELS=C:\Users\11093\AppData\Local\Temp\WeChatAuto.SDK-codex-research\Tools\models"

if not exist "%DOTNET_EXE%" (
    echo [ERROR] Bundled .NET was not found:
    echo %DOTNET_EXE%
    pause
    exit /b 1
)

if not exist "%PROJECT%" (
    echo [ERROR] Project file was not found:
    echo %PROJECT%
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq wechat-ollama-auto-reply.exe" 2>nul | find /I "wechat-ollama-auto-reply.exe" >nul
if not errorlevel 1 (
    echo [INFO] The auto-reply service is already running.
    pause
    exit /b 2
)

if exist "%PROJECT_MODELS%\ch_PP-OCRv5_mobile_det.onnx" (
    set "AICHAT_OCR_MODELS_DIR=%PROJECT_MODELS%"
) else if exist "%FALLBACK_MODELS%\ch_PP-OCRv5_mobile_det.onnx" (
    set "AICHAT_OCR_MODELS_DIR=%FALLBACK_MODELS%"
) else (
    echo [ERROR] OCR models were not found.
    echo Put the OCR model files in: %PROJECT_MODELS%
    pause
    exit /b 1
)

set "AICHAT_ALLOW_ALL_UNMUTED_CHATS=true"
set "AICHAT_PROCESS_EXISTING_UNREAD=true"

echo ============================================================
echo WeChat Ollama AI Auto Reply
echo - Replies to private text messages
echo - Replies to unmuted group text messages
echo - Processes unread text messages already visible at startup
echo - Ignores muted groups, official accounts and system chats
echo - Press Ctrl+C to stop
echo ============================================================
echo.

pushd "%ROOT%"
"%DOTNET_EXE%" run --project "%PROJECT%" -- %*
set "EXIT_CODE=%ERRORLEVEL%"
popd

if not "%~1"=="" exit /b %EXIT_CODE%

echo.
if "%EXIT_CODE%"=="0" (
    echo [INFO] The auto-reply service has stopped.
) else (
    echo [ERROR] The service exited with code %EXIT_CODE%.
)
pause
exit /b %EXIT_CODE%
