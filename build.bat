@echo off
rem ============================================================
rem  Сборка CavaWin (cava для Windows 11, WPF-приложение)
rem  Требуется .NET SDK: https://dotnet.microsoft.com/download
rem ============================================================
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ОШИБКА] .NET SDK не найден. Установите с https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

echo Собираю Release...
dotnet build cava.csproj -c Release
if errorlevel 1 (
  echo.
  echo [ОШИБКА] Сборка не удалась, см. сообщения выше.
  pause
  exit /b 1
)

echo.
echo Готово. Приложение: bin\Release\net8.0-windows10.0.19041.0\cava.exe
echo Запуск: двойной клик по cava.exe
echo.
choice /C YN /M "Запустить сейчас"
if errorlevel 2 exit /b 0
start "" "bin\Release\net8.0-windows10.0.19041.0\cava.exe"