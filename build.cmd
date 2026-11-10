@echo off
setlocal
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

if exist "%~dp0dist\win-x64\VbeeSrtStudio.exe" del "%~dp0dist\win-x64\VbeeSrtStudio.exe"
if exist "%~dp0dist\win-x64\START.bat" del "%~dp0dist\win-x64\START.bat"
if exist "%~dp0dist\win-x64\VbeeSrtStudio-portable.zip" del "%~dp0dist\win-x64\VbeeSrtStudio-portable.zip"
if exist "%~dp0VbeeSrtStudio-portable.zip" del "%~dp0VbeeSrtStudio-portable.zip"

dotnet publish "%~dp0VbeeSrtStudio.csproj" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None ^
  --output "%~dp0dist\win-x64"
if errorlevel 1 exit /b %errorlevel%

copy /Y "%~dp0README.md" "%~dp0dist\win-x64\README.md" >nul
if errorlevel 1 exit /b %errorlevel%
copy /Y "%~dp0VbeeSrtStudio.ps1" "%~dp0dist\win-x64\VbeeSrtStudio.ps1" >nul
if errorlevel 1 exit /b %errorlevel%
powershell.exe -NoProfile -Command "$files = @('%~dp0dist\win-x64\Vbee SRT Studio.exe','%~dp0dist\win-x64\README.md','%~dp0dist\win-x64\VbeeSrtStudio.ps1'); Compress-Archive -LiteralPath $files -DestinationPath '%~dp0Vbee-SRT-Studio-Windows-x64.zip' -CompressionLevel Optimal -Force"
if errorlevel 1 exit /b %errorlevel%

echo Portable app: %~dp0dist\win-x64\Vbee SRT Studio.exe
echo PowerShell:   %~dp0dist\win-x64\VbeeSrtStudio.ps1
echo ZIP package:  %~dp0Vbee-SRT-Studio-Windows-x64.zip
