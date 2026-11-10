@echo off
setlocal
pushd "%~dp0"
powershell.exe -NoProfile -File "%~dp0VbeeSrtStudio.ps1" %*
set "scriptExitCode=%ERRORLEVEL%"
popd
echo.
if not "%scriptExitCode%"=="0" (
  echo Script ket thuc voi ma loi %scriptExitCode%.
  echo Neu PowerShell bao "running scripts is disabled", Windows dang chan script nay theo chinh sach hien tai.
  echo File BAT khong the vuot qua chinh sach. Khong tat hoac ha muc bao ve.
  echo Hay dung EXE neu duoc phep, hoac hoi quan tri vien de co ban script da ky duoc phe duyet.
)
pause
exit /b %scriptExitCode%
