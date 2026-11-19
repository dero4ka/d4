# Автоустановка D4: .NET 8 SDK -> сборка -> копирование -> ярлыки -> запуск
$ErrorActionPreference = "Continue"

$Root       = Split-Path -Parent $PSScriptRoot
$Proj       = Join-Path $Root "app\D4.csproj"
$InstallDir = Join-Path $env:LOCALAPPDATA "D4\app"
$Exe        = Join-Path $InstallDir "D4.exe"

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
}

function Has-Sdk8 {
    try {
        $list = & dotnet --list-sdks 2>$null
        return [bool]($list | Where-Object { $_ -match '^8\.' })
    } catch { return $false }
}

# Возвращает панель Windows; если старая версия D4 не понимает --restore-taskbar и просто запускается — закрываем её
function Restore-Taskbar($exe) {
    if (-not (Test-Path $exe)) { return }
    $p = Start-Process $exe -ArgumentList "--restore-taskbar" -PassThru
    if (-not $p.WaitForExit(6000)) { $p.Kill() }
    Get-Process -Name "D4" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

function Ask-Yes($question) {
    $a = Read-Host "$question [Y/n]"
    return ($a -eq "" -or $a -match '^(y|Y|д|Д)')
}

Write-Host ""
Write-Host "=== Установка D4 ===" -ForegroundColor Cyan

if (-not (Test-Path $Proj)) {
    Write-Host "Не найден файл app\D4.csproj. Запускайте install-d4.bat из папки проекта." -ForegroundColor Red
    exit 1
}

# 1. .NET 8 SDK
if (-not (Has-Sdk8)) {
    Write-Host "Нужен .NET 8 SDK, устанавливаю через winget (может появиться окно с запросом прав)..."
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
        Refresh-Path
    } else {
        Write-Host "winget не найден. Установите .NET 8 SDK вручную: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
        Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    }
    if (-not (Has-Sdk8)) {
        Write-Host ".NET 8 установлен, но окно его ещё не видит. Закройте это окно и запустите install-d4.bat ещё раз." -ForegroundColor Yellow
        exit 1
    }
}
Write-Host "[ok] .NET 8 SDK найден" -ForegroundColor Green

# 2. Закрываем запущенный D4, чтобы можно было заменить файлы
Get-Process -Name "D4" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
# принудительная остановка не возвращает панель Windows — возвращаем сами
Restore-Taskbar $Exe

# 3. Сборка прямо в папку установки
Write-Host "Собираю D4 (первый раз это занимает 1-2 минуты)..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
& dotnet publish $Proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $InstallDir
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $Exe)) {
    Write-Host "Сборка не удалась. Скопируйте текст ошибки выше и пришлите его, исправим." -ForegroundColor Red
    exit 1
}
Write-Host "[ok] D4 собран: $Exe" -ForegroundColor Green

# 4. Ярлыки
function New-Shortcut($path, $target, $workDir) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $target
    $lnk.WorkingDirectory = $workDir
    $lnk.Description = "D4"
    $lnk.Save()
}
New-Shortcut (Join-Path ([Environment]::GetFolderPath("Desktop")) "D4.lnk") $Exe $InstallDir
New-Shortcut (Join-Path ([Environment]::GetFolderPath("Programs")) "D4.lnk") $Exe $InstallDir
Write-Host "[ok] Ярлыки созданы на рабочем столе и в меню Пуск" -ForegroundColor Green

# 5. Автозапуск
if (Ask-Yes "Запускать D4 вместе с Windows?") {
    $key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $key -Name "D4" -Value ('"' + $Exe + '"')
    Write-Host "[ok] Автозапуск включён" -ForegroundColor Green
}

# 6. Запуск
if (Ask-Yes "Запустить D4 сейчас?") {
    Start-Process $Exe -WorkingDirectory $InstallDir
}

Write-Host ""
Write-Host "Готово. Настройки D4 открываются двойным щелчком по значку в трее." -ForegroundColor Cyan
