# Shared by the portable launcher and its provisioning checks. Requires PowerShell 5.1+.
Set-StrictMode -Version Latest

function Assert-PortablePlatform {
    param([string]$Root)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
            -not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess -or
            $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
        throw 'This portable bundle requires Windows 11 x64 and 64-bit PowerShell.'
    }
    if ($Root.StartsWith('\\')) { throw 'Extract the project to a local drive. WebView2 cannot run from a UNC/network path.' }
}

function Invoke-PortableTool {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($File)) failed (exit $LASTEXITCODE). See the output above." }
}

function Test-PortableTool {
    param([string]$File, [string[]]$Arguments, [string]$ExpectedOutput)
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) { return $false }
    try {
        $output = & $File @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { return $false }
        if (-not $ExpectedOutput) { return $true }
        $expectedPattern = '^' + [regex]::Escape($ExpectedOutput) + '(?:\s+\([^\r\n]+\))?$'
        return (($output -join "`n").Trim() -match $expectedPattern)
    } catch { return $false }
}

function Find-PortablePython {
    param([string]$Uv, [string]$Version)
    $python = & $Uv python find --managed-python --no-project $Version
    if ($LASTEXITCODE -ne 0 -or -not $python) { throw 'Portable Python could not be located.' }
    return [string]$python
}

function Receive-PortableArchive {
    param($Spec, [string]$Destination)
    $validHash = if ($Spec.algorithm -eq 'SHA512') { '^[a-fA-F0-9]{128}$' } else { '^[a-fA-F0-9]{64}$' }
    if ($Spec.algorithm -notin @('SHA256', 'SHA512') -or $Spec.hash -notmatch $validHash) {
        throw 'Invalid runtime checksum in portable-runtimes.json.'
    }
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-FileHash -LiteralPath $Destination -Algorithm $Spec.algorithm).Hash -eq $Spec.hash) { return }
        Remove-Item -LiteralPath $Destination -Force
    }
    if (([Uri]$Spec.url).Scheme -ne 'https') { throw 'Runtime downloads require HTTPS.' }
    $partial = "$Destination.partial"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Write-Host "Downloading $([IO.Path]::GetFileName(([Uri]$Spec.url).AbsolutePath)) (attempt $attempt/3)..."
            Invoke-WebRequest -UseBasicParsing -Uri $Spec.url -OutFile $partial -TimeoutSec 600
            if ((Get-FileHash -LiteralPath $partial -Algorithm $Spec.algorithm).Hash -ne $Spec.hash) {
                throw 'Downloaded runtime checksum does not match. The file will not be used.'
            }
            Move-Item -LiteralPath $partial -Destination $Destination -Force
            return
        } catch {
            Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds 2
        }
    }
}

function Expand-PortableZip {
    param([string]$Archive, [string]$Destination)
    $staging = "$Destination.extracting"
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    try {
        Expand-Archive -LiteralPath $Archive -DestinationPath $staging -Force
        Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $staging -Destination $Destination
    } finally {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-PortableBuildHash {
    param([string]$Root)
    $files = @(
        Get-ChildItem -LiteralPath (Join-Path $Root 'windows') -Recurse -File |
            Where-Object { $_.Extension -in @('.cs', '.csproj', '.manifest') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        Get-ChildItem -LiteralPath (Join-Path $Root 'streamerbot') -Recurse -File |
            Where-Object { $_.Extension -in @('.py', '.html', '.js', '.css', '.svg') -and $_.FullName -notmatch '[\\/]__pycache__[\\/]' }
        foreach ($name in @('pyproject.toml', 'uv.lock', 'requirements-port.txt', 'avatar.png', 'global.json', 'windows/Setup-Published.ps1')) {
            Get-Item -LiteralPath (Join-Path $Root $name)
        }
    )
    $lines = foreach ($file in ($files | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($Root.TrimEnd('\', '/').Length).Replace('\', '/')
        "$relative $((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)"
    }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))).Replace('-', '') }
    finally { $algorithm.Dispose() }
}

function Test-PortableDesktop {
    param([string]$Desktop, [string]$Stamp, [string]$Hash)
    $directory = Split-Path -Parent $Desktop
    foreach ($path in @($Desktop, (Join-Path $directory 'Microsoft.UI.Xaml.dll'),
            (Join-Path $directory 'StreamerBot.Desktop.dll'), (Join-Path $directory 'StreamerBot.Desktop.runtimeconfig.json'),
            (Join-Path $directory 'Worker/streamerbot/__main__.py'), $Stamp)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    }
    return ((Get-Content -LiteralPath $Stamp -Raw).Trim() -eq $Hash)
}

function Initialize-PortablePython {
    param([string]$Root, [string]$HomePath, $Manifest, [switch]$Repair)
    $uv = Join-Path $HomePath 'uv/uv.exe'
    if ($Repair -or -not (Test-PortableTool $uv @('--version') -ExpectedOutput ("uv " + $Manifest.uv.version))) {
        $archive = Join-Path $HomePath 'downloads/uv.zip'
        Receive-PortableArchive $Manifest.uv $archive
        Expand-PortableZip $archive (Join-Path $HomePath 'uv')
        if (-not (Test-PortableTool $uv @('--version') -ExpectedOutput ("uv " + $Manifest.uv.version))) { throw 'The portable uv executable could not run at the required version.' }
    }
    $env:UV_PYTHON_INSTALL_DIR = Join-Path $HomePath 'python'
    $env:UV_CACHE_DIR = Join-Path $HomePath 'cache/uv'
    $env:UV_PROJECT_ENVIRONMENT = Join-Path $HomePath 'venv'
    # uv downloads its pinned, checksum-verified standalone CPython distribution.
    $pythonArgs = @('python', 'install', $Manifest.python, '--no-bin', '--no-registry')
    if ($Repair) { $pythonArgs += '--reinstall' }
    $pythonProbe = @('-c', 'import sys, struct, ssl, sqlite3, venv; assert sys.version_info[:2] == (3,12) and struct.calcsize(''P'') == 8')
    try {
        Invoke-PortableTool $uv $pythonArgs
        $basePython = Find-PortablePython $uv $Manifest.python
        if (-not (Test-PortableTool $basePython $pythonProbe)) { throw 'Python runtime health check failed.' }
    } catch {
        Write-Host 'Repairing the portable Python runtime...'
        Invoke-PortableTool $uv @('python', 'install', $Manifest.python, '--no-bin', '--no-registry', '--reinstall')
        $basePython = Find-PortablePython $uv $Manifest.python
        if (-not (Test-PortableTool $basePython $pythonProbe)) { throw 'Portable Python is still unable to run after repair.' }
    }
    $python = Join-Path $env:UV_PROJECT_ENVIRONMENT 'Scripts/python.exe'
    if (-not (Test-PortableTool $python $pythonProbe)) {
        Invoke-PortableTool $uv @('venv', '--clear', '--python', $Manifest.python, '--managed-python', $env:UV_PROJECT_ENVIRONMENT)
    }
    $sync = @('sync', '--project', $Root, '--frozen', '--all-extras', '--no-dev', '--python', $Manifest.python, '--managed-python')
    if ($Repair) { $sync += '--reinstall' }
    try { Invoke-PortableTool $uv $sync }
    catch {
        Write-Host 'Retrying Python dependency setup with a full package reinstall...'
        if ($sync -notcontains '--reinstall') { $sync += '--reinstall' }
        Invoke-PortableTool $uv $sync
    }
    $importProbe = @('-c', 'import aiohttp, PIL.Image, mss, edge_tts, pygame, google.auth, requests, streamerbot; from streamerbot import server')
    if (-not (Test-PortableTool $python $importProbe)) {
        Write-Host 'Repairing missing or damaged Python packages...'
        if ($sync -notcontains '--reinstall') { $sync += '--reinstall' }
        Invoke-PortableTool $uv $sync
        if (-not (Test-PortableTool $python $importProbe)) { throw 'Python package checks still fail after repair.' }
    }
    Invoke-PortableTool $uv @('pip', 'check', '--python', $python)
    return $python
}

function Initialize-PortableDesktop {
    param([string]$Root, [string]$HomePath, $Manifest, [switch]$Repair)
    $desktop = Join-Path $Root 'windows/StreamerBot.Desktop/bin/Release/net10.0-windows10.0.19041.0/win-x64/StreamerBot.Desktop.exe'
    $stamp = Join-Path $HomePath 'desktop-build.sha256'
    $hash = Get-PortableBuildHash $Root
    if ($Repair -or -not (Test-PortableDesktop $desktop $stamp $hash)) {
        $dotnet = Join-Path $HomePath 'dotnet/dotnet.exe'
        $env:DOTNET_ROOT = Join-Path $HomePath 'dotnet'
        $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
        $env:DOTNET_MULTILEVEL_LOOKUP = '0'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        $env:DOTNET_CLI_HOME = Join-Path $HomePath 'cache/dotnet'
        $env:NUGET_PACKAGES = Join-Path $HomePath 'cache/nuget'
        if ($Repair -or -not (Test-PortableTool $dotnet @('--version') -ExpectedOutput $Manifest.dotnet.version)) {
            $archive = Join-Path $HomePath 'downloads/dotnet.zip'
            Receive-PortableArchive $Manifest.dotnet $archive
            Expand-PortableZip $archive $env:DOTNET_ROOT
        }
        $sdk = & $dotnet --version
        if ($LASTEXITCODE -ne 0 -or $sdk -ne $Manifest.dotnet.version) { throw 'The portable .NET SDK could not run at the required version.' }
        $project = Join-Path $Root 'windows/StreamerBot.Desktop/StreamerBot.Desktop.csproj'
        try { Invoke-PortableTool $dotnet @('build', $project, '-c', 'Release', '--nologo') }
        catch {
            Write-Host 'Retrying the desktop build with a fresh package restore...'
            Invoke-PortableTool $dotnet @('restore', $project, '--force', '--no-cache')
            Invoke-PortableTool $dotnet @('build', $project, '-c', 'Release', '--no-incremental', '--nologo')
        }
        # Mark successful builds only; a failed build is retried next launch.
        Set-Content -LiteralPath $stamp -Value $hash -Encoding ASCII
        if (-not (Test-PortableDesktop $desktop $stamp $hash)) {
            Remove-Item -LiteralPath $stamp -Force
            throw 'The build completed but required desktop files are missing.'
        }
    }
    return $desktop
}

function Initialize-PortableWebView {
    param([string]$HomePath, $Spec, [switch]$Repair)
    $folder = Join-Path $HomePath 'webview2'
    $stamp = Join-Path $folder 'runtime-version.txt'
    if ($Repair -or -not (Test-Path -LiteralPath (Join-Path $folder 'msedgewebview2.exe')) -or
            -not (Test-Path -LiteralPath (Join-Path $folder 'msedge.dll')) -or
            -not (Test-Path -LiteralPath $stamp) -or (Get-Content -LiteralPath $stamp -Raw).Trim() -ne $Spec.version) {
        $archive = Join-Path $HomePath 'downloads/webview2.cab'
        Receive-PortableArchive $Spec $archive
        $staging = Join-Path $HomePath 'webview2.extracting'
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $staging -Force | Out-Null
        try {
            Invoke-PortableTool (Join-Path $env:SystemRoot 'System32/expand.exe') @('-F:*', $archive, $staging)
            $browser = @(Get-ChildItem -LiteralPath $staging -Filter msedgewebview2.exe -Recurse -File)
            if ($browser.Count -ne 1 -or -not (Test-Path -LiteralPath (Join-Path $browser[0].DirectoryName 'msedge.dll'))) {
                throw 'The extracted WebView2 runtime is incomplete.'
            }
            Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
            Move-Item -LiteralPath $browser[0].DirectoryName -Destination $folder
            Set-Content -LiteralPath $stamp -Value $Spec.version -Encoding ASCII
        } finally {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    # Required by Microsoft's fixed-runtime sandbox guidance; only this runtime folder is granted read/execute.
    Invoke-PortableTool (Join-Path $env:SystemRoot 'System32/icacls.exe') @($folder, '/grant', '*S-1-15-2-2:(OI)(CI)(RX)', '*S-1-15-2-1:(OI)(CI)(RX)')
    return $folder
}
