[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/native'), [string]$MakeNsis = 'makensis.exe')
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$SmokeRoot = $null
$Build = Join-Path ([IO.Path]::GetTempPath()) ('StreamerBotNative-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Build, $OutputDirectory -Force | Out-Null
function Invoke-Checked { param([string]$Tool, [string[]]$Arguments) & $Tool @Arguments; if ($LASTEXITCODE -ne 0) { throw "$Tool failed with exit $LASTEXITCODE" } }
try {
    Push-Location $Root
    Invoke-Checked 'python' @('tests/test_rvc_voice.py','-v')
    Invoke-Checked 'python' @('tests/test_original_voice_functions.py','-v')
    Invoke-Checked 'python' @('windows/voice/test_worker.py','-v')
    Invoke-Checked 'python' @('windows/voice/test_video_paths.py','-v')
    Invoke-Checked 'dotnet' @('run','--project','windows/StreamerBot.Native.Check','--',(Get-Command python).Source,(Join-Path $Root 'windows/voice/worker.py'))
    Invoke-Checked 'dotnet' @('publish','windows/StreamerBot.Native','-c','Release','-o',(Join-Path $Build 'published'))
    $SmokeRoot = Join-Path $Build 'smoke'
    New-Item -ItemType Directory -Path $SmokeRoot -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $Build 'published') -File | Select-Object Name,Length | ConvertTo-Json | Set-Content (Join-Path $SmokeRoot 'published-files.json')
    $Smoke = Start-Process -FilePath (Join-Path $Build 'published/StreamerBot.Native.exe') -ArgumentList @('--smoke-test', ('"' + $SmokeRoot + '"')) -RedirectStandardError (Join-Path $SmokeRoot 'stderr.txt') -RedirectStandardOutput (Join-Path $SmokeRoot 'stdout.txt') -PassThru
    if (-not $Smoke.WaitForExit(30000)) { $Smoke.Kill(); throw 'Native UI smoke test timed out' }
    $Smoke.Refresh()
    Write-Host ('Smoke exit code: ' + $Smoke.ExitCode)
    Get-ChildItem $SmokeRoot | Format-Table Name,Length
    Get-Content (Join-Path $SmokeRoot 'stderr.txt') -ErrorAction SilentlyContinue
    Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=(Get-Date).AddMinutes(-2) } -ErrorAction SilentlyContinue | Where-Object { $_.ProviderName -in @('Application Error','.NET Runtime','Windows Error Reporting') } | Select-Object TimeCreated, ProviderName, Message | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $SmokeRoot 'windows-events.json')
    if ($Smoke.ExitCode -ne 0 -or -not (Test-Path (Join-Path $SmokeRoot 'smoke-ready.txt')) -or -not (Test-Path (Join-Path $SmokeRoot 'original-workflows-ready.txt')) -or -not (Test-Path (Join-Path $SmokeRoot 'keyboard-ready.txt')) -or -not (Test-Path (Join-Path $SmokeRoot 'overlay-drag-ready.txt')) -or -not (Test-Path (Join-Path $SmokeRoot 'paths-ready.txt'))) { throw 'Native UI did not load and lay out successfully' }
    Write-Host 'Native WinUI startup / layout / shutdown smoke test passed.'
    @'
# Build validation
Native core integration checks, worker tests, full Windows x64 publish and a native
window startup / layout / shutdown smoke test completed before packaging.
The installer contains self-contained .NET and Windows App SDK runtime files.
This build does not certify complete visual behavior, audio devices, GPU inference or live service accounts.
See NATIVE-PARITY.md for incomplete original workflows. No user secrets or models are included.
'@ | Set-Content -LiteralPath (Join-Path $Build 'published/BUILD-VALIDATION.md') -Encoding UTF8
    Invoke-Checked 'python' @((Join-Path $PSScriptRoot 'installer/stage_native.py'),(Join-Path $Build 'published'),(Join-Path $Build 'payload'))
    $Installer = Join-Path $OutputDirectory 'StreamerBot-Native-Setup.exe'
    Invoke-Checked $MakeNsis @('/V2',('/DPAYLOAD=' + (Join-Path $Build 'payload')),('/DINSTALLER_OUT=' + $Installer),('/DUNINSTALL_INCLUDE=' + (Join-Path $Build 'uninstall_payload.nsh')),(Join-Path $PSScriptRoot 'installer/StreamerBot.Native.nsi'))
    $Update = Join-Path $OutputDirectory 'StreamerBot-Native-Update.exe'
    Invoke-Checked $MakeNsis @('/V2','/DUPDATE_ONLY',('/DPAYLOAD=' + (Join-Path $Build 'payload')),('/DINSTALLER_OUT=' + $Update),('/DUNINSTALL_INCLUDE=' + (Join-Path $Build 'uninstall_payload.nsh')),(Join-Path $PSScriptRoot 'installer/StreamerBot.Native.nsi'))
    # Only disposable CI runners may install/uninstall during a build.
    if ($env:GITHUB_ACTIONS -eq 'true') {
    $Installed = Join-Path $env:LOCALAPPDATA 'Programs/StreamerBotNative'
    if ((Test-Path $Installed) -or (Test-Path 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/StreamerBotNative')) { throw 'Installer verification requires a clean disposable CI user' }
    $FreshUpdate = Start-Process -FilePath $Update -ArgumentList '/S' -PassThru -Wait
    if ($FreshUpdate.ExitCode -eq 0 -or (Test-Path $Installed)) { throw 'Update EXE must require an existing installation' }
    $InstallProcess = Start-Process -FilePath $Installer -ArgumentList '/S' -PassThru -Wait
    if ($InstallProcess.ExitCode -ne 0 -or -not (Test-Path (Join-Path $Installed 'StreamerBot.Native.exe'))) { throw 'Installer did not install the native executable' }
    $ActualData = Join-Path $env:LOCALAPPDATA 'StreamerBotNative'
    New-Item -ItemType Directory -Path $ActualData -Force | Out-Null
    $PreservedProfile = Join-Path $ActualData 'profile.json'
    $PreservedCache = Join-Path $ActualData 'selected cache'
    New-Item -ItemType Directory -Path $PreservedCache -Force | Out-Null
    @{ voice_cache_path=$PreservedCache; future_setting=@{ retained=$true } } | ConvertTo-Json | Set-Content $PreservedProfile
    $ProfileHash = (Get-FileHash $PreservedProfile).Hash
    $CacheFixture = Join-Path $PreservedCache 'keep.wav'
    'Synthetic cache fixture' | Set-Content $CacheFixture
    $CacheHash = (Get-FileHash $CacheFixture).Hash
    $AddedFile = Join-Path $Installed 'user-added-test.txt'
    'Synthetic user-added file; update/uninstall must preserve it.' | Set-Content -LiteralPath $AddedFile
    $LockedExe = [IO.File]::Open((Join-Path $Installed 'StreamerBot.Native.exe'), 'Open', 'Read', 'Read')
    try {
        $BlockedUpdate = Start-Process -FilePath $Update -ArgumentList '/S' -PassThru -Wait
        if ($BlockedUpdate.ExitCode -eq 0) { throw 'Update did not reject a locked running-app executable' }
    } finally { $LockedExe.Dispose() }
    $CoreDll = Join-Path $Installed 'StreamerBot.Native.Core.dll'
    $ExpectedCoreHash = (Get-FileHash $CoreDll).Hash
    'Synthetic old payload to be replaced' | Set-Content $CoreDll
    Set-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/StreamerBotNative' -Name DisplayVersion -Value '0.3.0'
    $UpdateProcess = Start-Process -FilePath $Update -ArgumentList '/S' -PassThru -Wait
    if ($UpdateProcess.ExitCode -ne 0 -or (Get-FileHash $CoreDll).Hash -ne $ExpectedCoreHash -or (Get-FileHash $PreservedProfile).Hash -ne $ProfileHash -or (Get-FileHash $CacheFixture).Hash -ne $CacheHash -or -not (Test-Path $AddedFile)) { throw 'Update failed payload replacement or settings/cache/user-file retention' }
    if ((Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/StreamerBotNative').DisplayVersion -ne '0.3.1') { throw 'Update version was not recorded' }
    Write-Host 'Actual update EXE / locked-app protection / profile-cache retention checks passed.'
    $InstalledSmoke = Join-Path $SmokeRoot 'installed-app'
    New-Item -ItemType Directory -Path $InstalledSmoke -Force | Out-Null
    $InstalledProcess = Start-Process -FilePath (Join-Path $Installed 'StreamerBot.Native.exe') -ArgumentList @('--smoke-test', ('"' + $InstalledSmoke + '"')) -PassThru
    if (-not $InstalledProcess.WaitForExit(30000)) { $InstalledProcess.Kill(); throw 'Installed native app smoke test timed out' }
    if ($InstalledProcess.ExitCode -ne 0 -or -not (Test-Path (Join-Path $InstalledSmoke 'smoke-ready.txt')) -or -not (Test-Path (Join-Path $InstalledSmoke 'original-workflows-ready.txt')) -or -not (Test-Path (Join-Path $InstalledSmoke 'keyboard-ready.txt')) -or -not (Test-Path (Join-Path $InstalledSmoke 'overlay-drag-ready.txt')) -or -not (Test-Path (Join-Path $InstalledSmoke 'paths-ready.txt'))) { throw 'Installed native app did not start successfully' }
    # Exercise the exact silent handoff used by Help -> Check for updates.
    $RunningApp = Start-Process -FilePath (Join-Path $Installed 'StreamerBot.Native.exe') -PassThru
    $RestartedApp = $null
    try {
        $Deadline = (Get-Date).AddSeconds(15)
        do { Start-Sleep -Milliseconds 200; $RunningApp.Refresh() } while (-not $RunningApp.HasExited -and $RunningApp.MainWindowHandle -eq 0 -and (Get-Date) -lt $Deadline)
        if ($RunningApp.HasExited -or $RunningApp.MainWindowHandle -eq 0) { throw 'Installed app did not open for update handoff' }
        $HandoffUpdate = Start-Process -FilePath $Update -ArgumentList @('/S','/RESTART',('/APP_PID=' + $RunningApp.Id)) -PassThru
        Start-Sleep -Milliseconds 500
        if ($HandoffUpdate.HasExited) { throw 'Update did not wait for the running application' }
        if (-not $RunningApp.CloseMainWindow() -or -not $RunningApp.WaitForExit(15000)) { throw 'Application did not close cleanly for update' }
        if (-not $HandoffUpdate.WaitForExit(30000) -or $HandoffUpdate.ExitCode -ne 0) { throw 'Silent update handoff failed' }
        $Deadline = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 200
            $RestartedApp = Get-Process -Name 'StreamerBot.Native' -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $RunningApp.Id -and $_.Path -eq (Join-Path $Installed 'StreamerBot.Native.exe') } | Select-Object -First 1
        } while ((-not $RestartedApp -or $RestartedApp.MainWindowHandle -eq 0) -and (Get-Date) -lt $Deadline)
        if (-not $RestartedApp -or $RestartedApp.MainWindowHandle -eq 0) { throw 'Update did not reopen the installed application' }
        if (-not $RestartedApp.CloseMainWindow() -or -not $RestartedApp.WaitForExit(15000)) { throw 'Restarted application did not close cleanly' }
    } finally {
        foreach ($AppProcess in @($RunningApp, $RestartedApp)) { if ($AppProcess -and -not $AppProcess.HasExited) { $AppProcess.Kill(); $AppProcess.WaitForExit() } }
    }
    $SavedAfterRestart = Get-Content $PreservedProfile -Raw | ConvertFrom-Json
    if ($SavedAfterRestart.voice_cache_path -ne $PreservedCache -or -not $SavedAfterRestart.future_setting.retained -or (Get-FileHash $CacheFixture).Hash -ne $CacheHash) { throw 'Update restart lost selected cache or unknown settings' }
    # Normal app close saves toolbar settings; retain that new baseline for uninstall.
    $ProfileHash = (Get-FileHash $PreservedProfile).Hash
    'Silent update waited for the running app and reopened it; selected cache and unknown settings retained.' | Set-Content (Join-Path $SmokeRoot 'update-ready.txt')
    Write-Host 'Actual silent update / running-app PID wait / restart checks passed.'
    $UninstallProcess = Start-Process -FilePath (Join-Path $Installed 'Uninstall.exe') -ArgumentList '/S' -PassThru -Wait
    $Deadline = (Get-Date).AddSeconds(30)
    while ((Test-Path (Join-Path $Installed 'StreamerBot.Native.exe')) -and (Get-Date) -lt $Deadline) { Start-Sleep -Milliseconds 250 }
    if ((Test-Path (Join-Path $Installed 'StreamerBot.Native.exe')) -or -not (Test-Path $AddedFile) -or -not (Test-Path (Join-Path $InstalledSmoke 'profile.json')) -or (Get-FileHash $PreservedProfile).Hash -ne $ProfileHash -or (Get-FileHash $CacheFixture).Hash -ne $CacheHash) { throw 'Uninstall failed payload removal or user-data retention' }
    Remove-Item -LiteralPath $AddedFile -Force
    Write-Host 'Actual installer / installed native app / uninstall data-retention checks passed.'
    }
    $Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Installer).Hash.ToLowerInvariant()
    $UpdateHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Update).Hash.ToLowerInvariant()
    @(($Hash + '  StreamerBot-Native-Setup.exe'), ($UpdateHash + '  StreamerBot-Native-Update.exe')) | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ASCII
    Copy-Item (Join-Path $Build 'payload/PAYLOAD-SHA256.json') $OutputDirectory
    Copy-Item (Join-Path $PSScriptRoot 'NATIVE-PARITY.md') $OutputDirectory
    Write-Host "Built $Installer"
} finally {
    if ($SmokeRoot -and (Test-Path -LiteralPath $SmokeRoot)) { Copy-Item -LiteralPath $SmokeRoot -Destination (Join-Path $OutputDirectory 'smoke') -Recurse -Force }
    Pop-Location; if (Test-Path -LiteralPath $Build) { Remove-Item -LiteralPath $Build -Recurse -Force } }
