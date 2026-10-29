# ====================================================================
# AUTOMATED GITHUB ACCOUNT SWITCHER & CODERABBIT SETUP INITIALIZER
# Automatically manages, generates, and verifies .coderabbit.yaml
# ====================================================================

# ====================================================================
# ASK FOR ACCOUNT / REPO DETAILS (nothing to edit in this file anymore)
# ====================================================================
function Ask($label, $default = "") {
    while ($true) {
        $suffix = ""
        if ($default) { $suffix = " [$default]" }
        $v = Read-Host "$label$suffix"
        if ([string]::IsNullOrWhiteSpace($v)) { $v = $default }
        if (-not [string]::IsNullOrWhiteSpace($v)) { return $v.Trim() }
        Write-Host "   This one is required." -ForegroundColor Red
    }
}

Clear-Host
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "   NEW PROJECT SETUP: GITHUB + CODERABBIT    " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Folder: $PSScriptRoot" -ForegroundColor Gray
Write-Host ""
Write-Host "--- GitHub ---" -ForegroundColor Green
$GitHubUser = Ask "GitHub username that OWNS the repo (or org name)"
$RepoName   = (Ask "Repository name (create it on GitHub first, EMPTY)").Replace(".git","")
$GitName    = Ask "Name for commits" $GitHubUser
$GitEmail   = Ask "Email linked to that GitHub account"

Write-Host ""
Write-Host "--- CodeRabbit ---" -ForegroundColor Green
Write-Host "(CodeRabbit has no password here: it signs in through the GitHub app.)" -ForegroundColor Gray
$CrProfile = Ask "Review style: chill or assertive" "assertive"
if ($CrProfile -notin @("chill","assertive")) { $CrProfile = "assertive" }
$CrFocus = Ask "One-line review focus for this repo" "Enforce clean code, robustness and sensible architecture."
$CrFocus = $CrFocus.Replace('"',"'")

$RepoUrl = "https://github.com/$GitHubUser/$RepoName.git"

Write-Host ""
Write-Host "---------------------------------------------" -ForegroundColor Cyan
Write-Host " Repo   : $RepoUrl"
Write-Host " Commit : $GitName <$GitEmail>"
Write-Host " Review : $CrProfile"
Write-Host "---------------------------------------------" -ForegroundColor Cyan
Write-Host "WARNING: this deletes any local .git history and FORCE-pushes," -ForegroundColor Yellow
Write-Host "         overwriting everything in that GitHub repo." -ForegroundColor Yellow
$go = Read-Host "Type YES to continue"
if ($go -ne "YES") { Write-Host "Cancelled."; Start-Sleep 2; exit }

# Always sign in fresh so the new account is used
$ClearOldLogin = $true

$ErrorActionPreference = "Continue"

# Always run from the folder this script lives in
Set-Location -Path $PSScriptRoot


$currentDir = $PSScriptRoot.Replace('\', '/')

try {
    git config --global --add safe.directory "$currentDir"

    # Any file bigger than this is skipped automatically (GitHub hard limit is 100 MB)
    $maxFileBytes = 90MB

    # Folders always skipped
    $skipFolders = @('portable', 'voice_cache', 'cache')

    # AUTOMATED CODERABBIT CONFIGURATION GENERATION & VERIFICATION
    Write-Host "`n[0/6] Automating .coderabbit.yaml verification..." -ForegroundColor Green
    $yamlPath = Join-Path $PSScriptRoot ".coderabbit.yaml"
    
    # Target instructions template tailored for your tool
    $yamlContent = @"
version: "1"
language: "en-US"
reviews:
  profile: "$CrProfile"
  system_instructions: |
    You are reviewing the $RepoName repository. $CrFocus
"@

    if (-not (Test-Path -Path $yamlPath)) {
        Write-Host "   -> .coderabbit.yaml missing! Creating automated template..." -ForegroundColor Yellow
        Set-Content -Path $yamlPath -Value $yamlContent -Encoding UTF8
    } else {
        Write-Host "   -> .coderabbit.yaml found! Verifying configuration properties..." -ForegroundColor Green
        # Simple structural check to verify version rules match CodeRabbit layout
        $contentCheck = Get-Content -Path $yamlPath
        if ($contentCheck -notmatch 'version:') {
            Write-Host "   -> Configuration outdated or corrupted! Re-generating fresh file..." -ForegroundColor DarkYellow
            Set-Content -Path $yamlPath -Value $yamlContent -Encoding UTF8
        }
    }

    Write-Host "`n[1/6] Updating Git identity properties..." -ForegroundColor Yellow
    git config --global user.name "$GitName"
    git config --global user.email "$GitEmail"

    if ($ClearOldLogin) {
        Write-Host "   -> Clearing saved GitHub login (you will sign in again)..." -ForegroundColor Yellow
        "protocol=https`nhost=github.com`n`n" | git credential reject
        cmdkey /delete:git:https://github.com 2>$null | Out-Null
    }

    # 2. Purge Local Tracking State Memory
    Write-Host "[2/6] Wiping old tracking records clean..." -ForegroundColor Yellow
    if (Test-Path -Path ".git") {
        Remove-Item -Recurse -Force .git -ErrorAction SilentlyContinue
    }

    # 3. Spin up fresh Git Environment
    Write-Host "[3/6] Instantiating pristine main workspace branch..." -ForegroundColor Yellow
    git init
    git branch -M main

    Remove-Item Env:\GIT_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:\GIT_WORK_TREE -ErrorAction SilentlyContinue
    git remote remove origin 2>$null

    # 4. Auto-detect oversized files and exclude them
    Write-Host "[4/6] Scanning for files too large for GitHub..." -ForegroundColor Yellow
    $excludeFile = Join-Path $PSScriptRoot ".git/info/exclude"
    New-Item -ItemType Directory -Force -Path (Split-Path $excludeFile) | Out-Null

    $excludeLines = @()
    foreach ($folder in $skipFolders) { $excludeLines += "/$folder/" }

    $rootLen = $PSScriptRoot.TrimEnd('\').Length + 1
    $bigFiles = Get-ChildItem -Path $PSScriptRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Length -gt $maxFileBytes -and
            $_.FullName -notlike "*\.git\*"
        }

    foreach ($f in $bigFiles) {
        $rel = $f.FullName.Substring($rootLen).Replace('\', '/')
        $excludeLines += "/$rel"
        $sizeMB = [math]::Round($f.Length / 1MB, 1)
        Write-Host "   Skipping large file: $rel ($sizeMB MB)" -ForegroundColor DarkYellow
    }

    Set-Content -Path $excludeFile -Value $excludeLines -Encoding ASCII

    # 5. Stage everything else (Including the newly verified .coderabbit.yaml)
    Write-Host "[5/6] Staging files..." -ForegroundColor Yellow
    git add -A

    $status = git status --porcelain
    if ($status) {
        git commit -m "feat: initial automated upload with auto-verified coderabbit config"

        # 6. Push to GitHub
        Write-Host "[6/6] Pushing to $RepoUrl ..." -ForegroundColor Yellow
        Write-Host "`nLaunching GitHub sign-in prompt..." -ForegroundColor Green

        # FORCE FIX: Completely literal single quotes prevent string drop bugs
        git remote add origin $RepoUrl
        git push -u origin main --force

        if ($LASTEXITCODE -eq 0) {
            Write-Host "`nSUCCESS! Your project has been pushed online." -ForegroundColor Green
            if ($bigFiles) {
                Write-Host "Note: the large files listed above were skipped (over GitHub's size limit)." -ForegroundColor DarkYellow
            }
        } else {
            Write-Host "`nPUSH FAILED (exit code $LASTEXITCODE). Check the error above." -ForegroundColor Red
        }
    } else {
        Write-Host "`nWARNING: Staging block is empty. No code modifications detected." -ForegroundColor Yellow
    }

} catch {
    Write-Host "`n[FATAL SCRIPT CRASH]: $_" -ForegroundColor Red
}

Write-Host "`n=============================================" -ForegroundColor Cyan
Write-Host "Process finished. Press ANY key to exit..." -ForegroundColor Cyan
$null = [System.Console]::ReadKey($true)
