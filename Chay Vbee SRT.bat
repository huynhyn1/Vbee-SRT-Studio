@echo off
setlocal
pushd "%~dp0"
powershell.exe -NoProfile -File "%~dp0VbeeSrtStudio.ps1" %*
set "scriptExitCode=%ERRORLEVEL%"
popd
echo.
if not "%scriptExitCode%"=="0" echo Script ket thuc voi ma loi %scriptExitCode%.
pause
exit /b %scriptExitCode%
