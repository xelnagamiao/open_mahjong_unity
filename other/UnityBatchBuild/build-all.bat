@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-all.ps1" %*
set "build_exit=%ERRORLEVEL%"
echo.
if not "%build_exit%"=="0" echo Build failed. See the error and log path above.
pause
exit /b %build_exit%
