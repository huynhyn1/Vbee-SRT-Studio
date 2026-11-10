@echo off
setlocal
set "APP=%~dp0VbeeSrtStudio.exe"
if not exist "%APP%" set "APP=%~dp0dist\win-x64\VbeeSrtStudio.exe"

if not exist "%APP%" (
  echo Khong tim thay VbeeSrtStudio.exe.
  echo Neu ban vua tai ma nguon, hay tai ban portable tai:
  echo https://github.com/huynhyn1/Vbee-SRT-Studio/releases/latest
  pause
  exit /b 1
)

start "" "%APP%"
