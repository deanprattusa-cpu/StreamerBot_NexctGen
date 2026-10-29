[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$DataRoot, [string]$RuntimeRoot, [string]$PythonPath, [switch]$Repair, [switch]$CudaOnly, [ValidateSet('auto','cuda','cpu')][string]$GpuMode='auto')
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Portable-Runtime.ps1')
try {
    Assert-PortablePlatform $Root
    $env:STREAMERBOT_GPU_MODE = $GpuMode
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'portable-runtimes.json') -Raw | ConvertFrom-Json
    $runtime = if ($RuntimeRoot) { [IO.Path]::GetFullPath($RuntimeRoot) } else { Join-Path $DataRoot 'Runtime' }
    New-Item -ItemType Directory -Path (Join-Path $runtime 'downloads') -Force | Out-Null
    $lock = [IO.File]::Open((Join-Path $runtime 'voice-setup.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        $private = Join-Path $runtime 'original'
        if ($PythonPath) {
            $python = [IO.Path]::GetFullPath($PythonPath)
            if (-not (Test-PortableTool $python @('-c','import sys, struct; assert sys.prefix != sys.base_prefix and struct.calcsize(''P'') == 8'))) {
                throw 'Select a dedicated 64-bit Python virtual environment for voice repair. System Python is not modified.'
            }
        } else {
        $uv = Join-Path $runtime 'uv/uv.exe'
        if ($Repair -or -not (Test-PortableTool $uv @('--version') -ExpectedOutput ('uv ' + $manifest.uv.version))) {
            $archive = Join-Path $runtime 'downloads/uv.zip'
            Receive-PortableArchive $manifest.uv $archive
            Expand-PortableZip $archive (Join-Path $runtime 'uv')
        }
        $env:UV_PYTHON_INSTALL_DIR = Join-Path $runtime 'python'
        $env:UV_CACHE_DIR = Join-Path $runtime 'cache/uv'
        $args = @('python','install',$manifest.original_python,'--no-bin','--no-registry')
        if ($Repair) { $args += '--reinstall' }
        Invoke-PortableTool $uv $args
        $python = Join-Path $private 'venv/Scripts/python.exe'
        if (-not (Test-PortableTool $python @('-c','import sys, struct, ssl; assert sys.version_info[:2] == (3,11) and struct.calcsize(''P'') == 8'))) {
            Invoke-PortableTool $uv @('venv','--seed','--clear','--managed-python','--python',$manifest.original_python,(Join-Path $private 'venv'))
        }
        }
        $env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $private 'browsers'
        $args = @('-u',(Join-Path $PSScriptRoot 'prepare_original.py'),'--state-dir',$private,'--gpu-mode',$GpuMode)
        if ($CudaOnly) { $args += '--repair-gpu' } elseif ($Repair) { $args += '--repair' }
        Invoke-PortableTool $python $args
        Write-Host 'Voice preparation passed. Return to the native app and test the selected engine.'
    } finally { $lock.Dispose() }
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
