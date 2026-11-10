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

echo Portable app: %~dp0dist\win-x64\VbeeSrtStudio.exe
