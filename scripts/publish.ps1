# Автовыкладка D4 на GitHub: репозиторий + сайт (GitHub Pages) + релиз с D4.exe
$ErrorActionPreference = "Continue"

$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
}

function Ask-Yes($question) {
    $a = Read-Host "$question [Y/n]"
    return ($a -eq "" -or $a -match '^(y|Y|д|Д)')
}

function Need-Tool($command, $wingetId, $title) {
    if (Get-Command $command -ErrorAction SilentlyContinue) { return }
    Write-Host "Устанавливаю $title..."
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Host "Не найден winget. Установите $title вручную и запустите файл снова." -ForegroundColor Red
        exit 1
    }
    winget install --id $wingetId -e --accept-source-agreements --accept-package-agreements
    Refresh-Path
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        Write-Host "$title установлен. Закройте это окно и запустите publish-github.bat ещё раз." -ForegroundColor Yellow
        exit 1
    }
}

Write-Host ""
Write-Host "=== Выкладка D4 на GitHub ===" -ForegroundColor Cyan

if (-not (Test-Path (Join-Path $Root "docs\index.html"))) {
    Write-Host "Не найден docs\index.html. Запускайте файл из папки проекта." -ForegroundColor Red
    exit 1
}

Need-Tool "git" "Git.Git" "Git"
Need-Tool "gh"  "GitHub.cli" "GitHub CLI"

# 1. Данные
$user = Read-Host "Ваш логин на GitHub"
if ([string]::IsNullOrWhiteSpace($user)) { Write-Host "Логин обязателен." -ForegroundColor Red; exit 1 }
$repo = Read-Host "Название репозитория (Enter = d4)"
if ([string]::IsNullOrWhiteSpace($repo)) { $repo = "d4" }
$email = Read-Host "Почта для связи, которая будет на сайте (Enter = не менять)"

# 2. Подставляем ссылки в сайт
$index = Join-Path $Root "docs\index.html"
$text = [IO.File]::ReadAllText($index, [Text.Encoding]::UTF8)
$text = $text.Replace("__GH_USER__", $user).Replace("__GH_REPO__", $repo)
if (-not [string]::IsNullOrWhiteSpace($email)) { $text = $text.Replace("hello@example.com", $email) }
[IO.File]::WriteAllText($index, $text, (New-Object Text.UTF8Encoding $false))
Write-Host "[ok] Ссылки на сайте обновлены" -ForegroundColor Green

# 3. Git: имя и почта автора коммитов
$gitName = (& git config --global user.name)
if ([string]::IsNullOrWhiteSpace($gitName)) {
    $n = Read-Host "Имя для коммитов"
    & git config --global user.name $n
}
$gitMail = (& git config --global user.email)
if ([string]::IsNullOrWhiteSpace($gitMail)) {
    $m = Read-Host "Почта для коммитов"
    & git config --global user.email $m
}

# 4. Вход в GitHub (откроется браузер)
& gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Сейчас откроется браузер для входа в GitHub..."
    & gh auth login --hostname github.com --git-protocol https --web
    if ($LASTEXITCODE -ne 0) { Write-Host "Вход не выполнен." -ForegroundColor Red; exit 1 }
}

# 5. Коммит
if (-not (Test-Path (Join-Path $Root ".git"))) { & git init | Out-Null }
& git add -A
& git commit -m "D4: site and app" | Out-Null
& git branch -M main

# 6. Создаём репозиторий и отправляем код
& gh repo create "$user/$repo" --public --source . --remote origin --push
if ($LASTEXITCODE -ne 0) {
    Write-Host "Не удалось создать репозиторий (возможно, он уже есть), пробую отправить в существующий..." -ForegroundColor Yellow
    & git remote remove origin 2>$null
    & git remote add origin "https://github.com/$user/$repo.git"
    & git push -u origin main
    if ($LASTEXITCODE -ne 0) { Write-Host "Отправка не удалась. Проверьте логин и название репозитория." -ForegroundColor Red; exit 1 }
}
Write-Host "[ok] Код загружен: https://github.com/$user/$repo" -ForegroundColor Green

# 7. Включаем сайт (GitHub Pages из папки docs)
& gh api --method POST "repos/$user/$repo/pages" -f "source[branch]=main" -f "source[path]=/docs" *> $null
if ($LASTEXITCODE -eq 0) {
    Write-Host "[ok] Сайт включён: https://$user.github.io/$repo/ (появится через 1-2 минуты)" -ForegroundColor Green
} else {
    Write-Host "Сайт не включился автоматически (возможно, уже включён)." -ForegroundColor Yellow
    Write-Host "Проверьте вручную: https://github.com/$user/$repo/settings/pages -> Deploy from a branch -> main -> /docs"
}

# 8. Релиз с D4.exe, чтобы работала кнопка «Скачать»
if (Ask-Yes "Собрать D4.exe и выложить в Releases (для кнопки скачивания на сайте)?") {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "Нет .NET SDK. Сначала запустите install-d4.bat, потом повторите выкладку." -ForegroundColor Yellow
    } else {
        $dist = Join-Path $Root "dist"
        & dotnet publish (Join-Path $Root "app\D4.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:Version=1.2.0 -o $dist
        $exe = Join-Path $dist "D4.exe"
        if ($LASTEXITCODE -eq 0 -and (Test-Path $exe)) {
            & gh release create "v1.2.0" $exe --repo "$user/$repo" --title "D4 1.2.0" --notes "D4 1.2: 12 widgets (weather, music, calendar, Pomodoro, currency, GitHub, Discord, checklist, snippets), 13 themes, glass mode, animated wallpapers, mini-folders."
            if ($LASTEXITCODE -ne 0) {
                & gh release upload "v1.2.0" $exe --repo "$user/$repo" --clobber
            }
            Write-Host "[ok] Релиз выложен: https://github.com/$user/$repo/releases" -ForegroundColor Green
        } else {
            Write-Host "Сборка не удалась, релиз пропущен." -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "Готово!" -ForegroundColor Cyan
Write-Host "Репозиторий: https://github.com/$user/$repo"
Write-Host "Сайт:        https://$user.github.io/$repo/"
