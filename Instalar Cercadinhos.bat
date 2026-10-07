@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "ferramentas\instalar.ps1"
if errorlevel 1 (
  echo.
  echo Deu erro. Veja a mensagem acima.
  pause
)
