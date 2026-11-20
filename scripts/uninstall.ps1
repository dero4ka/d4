# Удаление D4
$ErrorActionPreference = "Continue"

# D4 работает с правами администратора — перезапускаем скрипт от администратора, если нужно
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -NoExit -File `"$PSCommandPath`""
    exit
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
Write-Host "=== Удаление D4 ===" -ForegroundColor Cyan

Get-Process -Name "D4" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
# принудительная остановка не возвращает панель Windows — возвращаем сами
$exe = Join-Path $env:LOCALAPPDATA "D4\app\D4.exe"
Restore-Taskbar $exe

$base = Join-Path $env:LOCALAPPDATA "D4"
if (Test-Path $base) { Remove-Item -Recurse -Force $base }

Remove-Item (Join-Path ([Environment]::GetFolderPath("Desktop")) "D4.lnk") -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath("Programs")) "D4.lnk") -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "D4" -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "D4" -Confirm:$false -ErrorAction SilentlyContinue
Write-Host "[ok] Программа, ярлыки и автозапуск удалены" -ForegroundColor Green

$data = Join-Path $env:APPDATA "D4"
if ((Test-Path $data) -and (Ask-Yes "Удалить и настройки (темы, заметки, мини-папки)?")) {
    Remove-Item -Recurse -Force $data
    Write-Host "[ok] Настройки удалены" -ForegroundColor Green
}

# Если D4 был закрыт аварийно, панель задач Windows могла остаться скрытой
if (Ask-Yes "Перезапустить проводник, чтобы вернуть панель задач Windows (если она пропала)?") {
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
}

Write-Host "Готово." -ForegroundColor Cyan
