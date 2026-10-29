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
    if ($Smoke.ExitCode -ne 0 -or -not (Test-Path (Join-Path $SmokeRoot 'smoke-ready.txt'))) { throw 'Native UI did not load and lay out successfully' }
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
    # Only disposable CI runners may install/uninstall during a build.
    if ($env:GITHUB_ACTIONS -eq 'true') {
    $Installed = Join-Path $env:LOCALAPPDATA 'Programs/StreamerBotNative'
    if ((Test-Path $Installed) -or (Test-Path 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/StreamerBotNative')) { throw 'Installer verification requires a clean disposable CI user' }
    $InstallProcess = Start-Process -FilePath $Installer -ArgumentList '/S' -PassThru -Wait
    if ($InstallProcess.ExitCode -ne 0 -or -not (Test-Path (Join-Path $Installed 'StreamerBot.Native.exe'))) { throw 'Installer did not install the native executable' }
    $InstalledSmoke = Join-Path $SmokeRoot 'installed-app'
    New-Item -ItemType Directory -Path $InstalledSmoke -Force | Out-Null
    $InstalledProcess = Start-Process -FilePath (Join-Path $Installed 'StreamerBot.Native.exe') -ArgumentList @('--smoke-test', ('"' + $InstalledSmoke + '"')) -PassThru
    if (-not $InstalledProcess.WaitForExit(30000)) { $InstalledProcess.Kill(); throw 'Installed native app smoke test timed out' }
    if ($InstalledProcess.ExitCode -ne 0 -or -not (Test-Path (Join-Path $InstalledSmoke 'smoke-ready.txt'))) { throw 'Installed native app did not start successfully' }
    $AddedFile = Join-Path $Installed 'user-added-test.txt'
    'Synthetic user-added file; uninstall must preserve it.' | Set-Content -LiteralPath $AddedFile
    $UninstallProcess = Start-Process -FilePath (Join-Path $Installed 'Uninstall.exe') -ArgumentList '/S' -PassThru -Wait
    $Deadline = (Get-Date).AddSeconds(30)
    while ((Test-Path (Join-Path $Installed 'StreamerBot.Native.exe')) -and (Get-Date) -lt $Deadline) { Start-Sleep -Milliseconds 250 }
    if ((Test-Path (Join-Path $Installed 'StreamerBot.Native.exe')) -or -not (Test-Path $AddedFile) -or -not (Test-Path (Join-Path $InstalledSmoke 'profile.json'))) { throw 'Uninstall failed payload removal or user-data retention' }
    Remove-Item -LiteralPath $AddedFile -Force
    Write-Host 'Actual installer / installed native app / uninstall data-retention checks passed.'
    }
    $Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Installer).Hash.ToLowerInvariant()
    ($Hash + '  StreamerBot-Native-Setup.exe') | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ASCII
    Copy-Item (Join-Path $Build 'payload/PAYLOAD-SHA256.json') $OutputDirectory
    Copy-Item (Join-Path $PSScriptRoot 'NATIVE-PARITY.md') $OutputDirectory
    Write-Host "Built $Installer"
} finally {
    if ($SmokeRoot -and (Test-Path -LiteralPath $SmokeRoot)) { Copy-Item -LiteralPath $SmokeRoot -Destination (Join-Path $OutputDirectory 'smoke') -Recurse -Force }
    Pop-Location; if (Test-Path -LiteralPath $Build) { Remove-Item -LiteralPath $Build -Recurse -Force } }
