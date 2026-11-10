@echo off
setlocal
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

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

copy /Y "%~dp0START.bat" "%~dp0dist\win-x64\START.bat" >nul
if errorlevel 1 exit /b %errorlevel%
copy /Y "%~dp0README.md" "%~dp0dist\win-x64\README.md" >nul
if errorlevel 1 exit /b %errorlevel%
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$files = @('%~dp0dist\win-x64\VbeeSrtStudio.exe','%~dp0dist\win-x64\START.bat','%~dp0dist\win-x64\README.md'); Compress-Archive -LiteralPath $files -DestinationPath '%~dp0VbeeSrtStudio-portable.zip' -CompressionLevel Optimal -Force"
if errorlevel 1 exit /b %errorlevel%

echo Portable app: %~dp0dist\win-x64\VbeeSrtStudio.exe
echo Launcher:     %~dp0START.bat
echo ZIP package:  %~dp0VbeeSrtStudio-portable.zip
