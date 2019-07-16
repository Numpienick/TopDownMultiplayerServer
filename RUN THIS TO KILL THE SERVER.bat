@echo off
taskkill /IM TopDownMultiplayer_Server.exe /F
echo Killing the Server...
timeout /T 5 /nobreak >nul