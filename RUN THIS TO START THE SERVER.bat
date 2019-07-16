@echo off
CD /D %~dp0\Build
start /b TopDownMultiplayer_Server.exe -batchmode -nographics
echo Starting the server...
timeout /T 5 /nobreak >nul