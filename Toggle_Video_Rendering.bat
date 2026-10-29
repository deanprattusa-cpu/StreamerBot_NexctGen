@echo off
title Video Rendering Toggle
:: THIS FORCES WINDOWS TO STAY IN THE BOT FOLDER!
cd /d "%~dp0"

set "VIDEO_STATE=%~dp0enable_video.txt"
if exist "%~dp0StreamerBot.Native.exe" (
    if not exist "%LOCALAPPDATA%\StreamerBotNative" mkdir "%LOCALAPPDATA%\StreamerBotNative"
    set "VIDEO_STATE=%LOCALAPPDATA%\StreamerBotNative\enable_video.txt"
)
if exist "%VIDEO_STATE%" (
del "%VIDEO_STATE%"
color 0c
echo ==========================================
echo [DISABLED] Video Rendering is now OFF.
echo The bot will only play existing cached videos.
echo ==========================================
) else (
echo 1 > "%VIDEO_STATE%"
color 0a
echo ==========================================
echo [ENABLED] Video Rendering is now ON.
echo The bot will generate new videos for new audio.
echo ==========================================
)
pause