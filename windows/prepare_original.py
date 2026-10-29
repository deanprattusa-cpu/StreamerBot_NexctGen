"""Prepare the original MVC application without converting or editing its settings/source."""

from __future__ import annotations

import argparse
import base64
import importlib.util
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CORE = ROOT / "windows/original-core-lock.txt"
CONSTRAINTS = ROOT / "windows/original-constraints.txt"
CORE_IMPORTS = (
    "tkinter",
    "numpy",
    "requests",
    "pygame",
    "keyboard",
    "groq",
    "google.generativeai",
    "speech_recognition",
    "sounddevice",
    "webview",
    "mss",
    "PIL",
    "openai",
    "dotenv",
    "google.auth",
    "google_auth_oauthlib",
    "googleapiclient.discovery",
    "google.cloud.texttospeech",
    "pydub",
    "pyttsx3",
    "edge_tts",
    "scipy.io.wavfile",
    "gradio_client",
    "imageio_ffmpeg",
    "playwright.sync_api",
    "selenium",
    "pynput",
)


def run(arguments, *, timeout=900, check=True):
    result = subprocess.run([sys.executable, *arguments], cwd=ROOT, timeout=timeout)
    if check and result.returncode:
        raise RuntimeError(f"Dependency command failed (exit {result.returncode}); see the output above.")
    return result


def pip(arguments, *, timeout=900, check=True):
    # Existing setup_deps.py uses --user. This adapter installs only inside our dedicated venv.
    arguments = [arg for arg in arguments if arg != "--user"]
    if arguments and arguments[0] == "install" and "-c" not in arguments:
        arguments += ["-c", str(CONSTRAINTS)]
    cache = os.environ.get("PIP_CACHE_DIR", str(ROOT / ".portable/original/pip-cache"))
    return run(["-m", "pip", "--isolated", "--cache-dir", cache, *arguments], timeout=timeout, check=check)


def load_installer(state):
    spec = importlib.util.spec_from_file_location("original_dependency_installer", ROOT / "setup_deps.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.REQ_FILE = str(ROOT / "requirements.txt")
    module.STATE_FILE = str(state / "dependency-state.json")
    module.CONSTRAINTS = str(CONSTRAINTS)
    module.pip = lambda args, timeout=None: pip(args, timeout=timeout or 900, check=False)
    return module


def missing_core():
    # Probe individually: one missing import must not obscure other missing voice/provider bindings.
    missing = []
    for module in CORE_IMPORTS:
        probe = subprocess.run(
            [sys.executable, "-c", f"import importlib; importlib.import_module({module!r})"],
            cwd=ROOT,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=90,
        )
        if probe.returncode:
            missing.append(module)
    return missing


def detect_nvidia():
    """Discover Windows NVIDIA hardware even when NVSMI is absent from PATH."""
    candidates = [shutil.which("nvidia-smi")]
    if sys.platform == "win32":
        candidates += [str(Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/nvidia-smi.exe")]
        for variable in ("ProgramW6432", "ProgramFiles"):
            candidates.append(
                str(
                    Path(os.environ.get(variable, r"C:\Program Files"))
                    / "NVIDIA Corporation/NVSMI/nvidia-smi.exe"
                )
            )
    for candidate in dict.fromkeys(candidates):
        if not candidate:
            continue
        try:
            result = subprocess.run(
                [candidate, "--query-gpu=name", "--format=csv,noheader"],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=20,
            )
            if result.returncode == 0 and result.stdout.strip():
                return {"nvidia": True, "source": "nvidia-smi", "devices": result.stdout.strip().splitlines()}
        except (OSError, subprocess.TimeoutExpired):
            pass
    if sys.platform != "win32":
        return {"nvidia": False, "source": "non-Windows host without NVSMI", "devices": []}
    powershell = (
        Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"
    )
    script = "[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); $ErrorActionPreference='Stop'; @(Get-CimInstance Win32_VideoController | Select-Object Name,PNPDeviceID) | ConvertTo-Json -Compress"
    encoded = base64.b64encode(script.encode("utf-16le")).decode("ascii")
    try:
        result = subprocess.run(
            [str(powershell), "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=30,
        )
        if result.returncode == 0:
            devices = json.loads(result.stdout.lstrip("\ufeff").strip())
            devices = [devices] if isinstance(devices, dict) else devices
            if isinstance(devices, list) and devices:
                names = [str(device.get("Name", "Unknown adapter")) for device in devices]
                nvidia = any(
                    "nvidia" in str(device.get("Name", "")).lower()
                    or "ven_10de" in str(device.get("PNPDeviceID", "")).lower()
                    for device in devices
                )
                return {"nvidia": nvidia, "source": "Windows video controllers", "devices": names}
    except (OSError, subprocess.TimeoutExpired, ValueError, TypeError, AttributeError):
        pass
    # An unavailable inventory must not silently convert an NVIDIA machine to CPU-only.
    return {"nvidia": None, "source": "Windows GPU inventory unavailable", "devices": []}


def probe_torch(cuda_required, cpu_only=False):
    script = """
import json, sys
result = {"status": "failed"}
try:
    import torch, torchvision, torchaudio
    result.update(torch=torch.__version__, torchvision=torchvision.__version__,
                  torchaudio=torchaudio.__version__, cuda_build=torch.version.cuda)
    for module, expected in ((torch, '2.5.1'), (torchvision, '0.20.1'), (torchaudio, '2.5.1')):
        if module.__version__.split('+')[0] != expected:
            raise RuntimeError('Mismatched Torch/vision/audio package versions')
    if sys.argv[1] == 'cuda':
        if torch.version.cuda != '12.4':
            raise RuntimeError('CUDA 12.4 Torch build required; installed build is ' + str(torch.version.cuda))
        if not torch.cuda.is_available():
            raise RuntimeError('CUDA runtime cannot use the GPU; check the NVIDIA driver and setup output')
        result['device'] = torch.cuda.get_device_name(0)
        value = torch.ones((8, 8), device='cuda')
        if (value @ value).sum().item() != 512:
            raise RuntimeError('CUDA tensor operation returned an unexpected result')
        torch.cuda.synchronize()
    elif sys.argv[1] == 'cpu' and torch.version.cuda is not None:
        raise RuntimeError('Explicit CPU mode requested a CPU Torch build')
    result['status'] = 'passed'
except Exception as error:
    result['reason'] = str(error)
print(json.dumps(result))
sys.exit(0 if result['status'] == 'passed' else 1)
"""
    try:
        result = subprocess.run(
            [sys.executable, "-c", script, "cuda" if cuda_required else "cpu" if cpu_only else "any"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=120,
        )
        report = json.loads(result.stdout.strip().splitlines()[-1])
        if result.returncode != 0:
            report["status"] = "failed"
        return report
    except (OSError, subprocess.TimeoutExpired, ValueError, IndexError):
        return {
            "status": "failed",
            "reason": "Torch probe could not finish; run Repair-CUDA.bat and inspect its output.",
        }


def prepare_torch(repair, mode="auto"):
    if mode not in {"auto", "cuda", "cpu"}:
        raise ValueError("GPU mode must be auto, cuda or cpu")
    hardware = detect_nvidia()
    gpu = mode == "cuda" or (mode == "auto" and hardware["nvidia"] is not False)
    context = {"mode": mode, "cuda_requested": gpu, "hardware": hardware}
    if not repair:
        existing = probe_torch(gpu, mode == "cpu")
        if existing["status"] == "passed":
            return {**existing, **context}
    variant = "cu124" if gpu else "cpu"
    index = f"https://download.pytorch.org/whl/{variant}"
    print(f"[Original] Installing {variant} Torch; hardware: {hardware['source']}", flush=True)
    try:
        # Exact local versions prevent a cached/PyPI CPU wheel satisfying a CUDA repair.
        pip(
            [
                "install",
                "--prefer-binary",
                "--force-reinstall",
                "--no-cache-dir",
                f"torch==2.5.1+{variant}",
                f"torchvision==0.20.1+{variant}",
                f"torchaudio==2.5.1+{variant}",
                "--index-url",
                index,
            ],
            timeout=3600,
        )
    except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        return {**context, "status": "failed", "reason": f"Torch install failed: {error}"}
    return {**probe_torch(gpu, mode == "cpu"), **context}


def finalize_torch(gpu, mode):
    # Optional legacy packages can replace Torch. Verify the final environment.
    final_gpu = probe_torch(gpu["cuda_requested"], mode == "cpu")
    if final_gpu["status"] != "passed" and gpu["status"] == "passed":
        print("[Original] Optional dependencies changed Torch; restoring the selected build.", flush=True)
        return prepare_torch(True, mode)
    return {**gpu, **final_gpu}


def save_gpu_report(state, report):
    (state / "gpu-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    if report["status"] == "passed":
        print(
            f"[Original] Torch verified: {report.get('torch')}; device: {report.get('device', 'CPU-compatible mode')}",
            flush=True,
        )
    else:
        print("[Original] GPU preparation FAILED: " + report.get("reason", "unknown error"), flush=True)
        print(
            "[Original] CUDA mode will not silently fall back to CPU. Run Repair-CUDA.bat; check the NVIDIA driver if the CUDA tensor probe fails.",
            flush=True,
        )


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("--state-dir", required=True, type=Path)
    parser.add_argument("--repair", action="store_true")
    parser.add_argument("--repair-gpu", action="store_true")
    parser.add_argument(
        "--gpu-mode", choices=("auto", "cuda", "cpu"), default=os.environ.get("STREAMERBOT_GPU_MODE", "auto")
    )
    args = parser.parse_args(argv)
    state = args.state_dir.resolve()
    state.mkdir(parents=True, exist_ok=True)
    if sys.prefix == sys.base_prefix:
        parser.error("Use the dedicated portable original venv; global Python installs are not modified.")
    os.environ["PIP_CACHE_DIR"] = str(state / "pip-cache")
    os.environ["PLAYWRIGHT_BROWSERS_PATH"] = str(state / "browsers")
    os.environ["PYGAME_HIDE_SUPPORT_PROMPT"] = "1"
    if args.repair_gpu:
        gpu = prepare_torch(True, args.gpu_mode)
        save_gpu_report(state, gpu)
        return 0 if gpu["status"] == "passed" else 1
    print("[Original] Preparing the original interface, provider and voice dependencies.", flush=True)
    pip(
        ["install", "--disable-pip-version-check", "pip==24.0", "setuptools==69.5.1", "wheel==0.43.0"],
        timeout=600,
    )
    install = [
        "install",
        "--prefer-binary",
        "--disable-pip-version-check",
        "--require-hashes",
        "-r",
        str(CORE),
    ]
    if args.repair:
        install.append("--force-reinstall")
    pip(install, timeout=1800)
    gpu = prepare_torch(args.repair, args.gpu_mode)
    installer = load_installer(state)
    # Preserve the original optional dependency inventory/timeout/progress logic.
    previous_argv = sys.argv
    try:
        sys.argv = [str(ROOT / "setup_deps.py"), *(["--retry"] if args.repair else [])]
        installer.main()
    finally:
        sys.argv = previous_argv
    gpu = finalize_torch(gpu, args.gpu_mode)
    save_gpu_report(state, gpu)
    failures = missing_core()
    browser = run(["-m", "playwright", "install", "chromium"], timeout=1800, check=False).returncode
    # Put imageio's bundled ffmpeg on the private PATH for the original subprocess workflows.
    try:
        import imageio_ffmpeg

        target = state / "bin/ffmpeg.exe"
        target.parent.mkdir(exist_ok=True)
        if not target.exists() or args.repair:
            shutil.copy2(imageio_ffmpeg.get_ffmpeg_exe(), target)
    except (ImportError, OSError):
        failures.append("imageio_ffmpeg binary")
    optional_failures = installer.load_state().get("failed", {})
    report = {
        "core_missing": failures,
        "optional_failures": optional_failures,
        "pytorch": gpu,
        "playwright_browser": "passed" if browser == 0 else "failed",
        "note": "Original controls/source retained. This report is dependency preparation, not live engine/device validation.",
    }
    (state / "dependency-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    if gpu["status"] != "passed":
        return 1
    if failures:
        print("[Original] Required imports still missing: " + ", ".join(failures), flush=True)
        return 1
    if optional_failures or gpu["status"] != "passed" or browser:
        print(
            "[Original] Some engine/render dependencies remain unavailable. All original controls are retained.",
            flush=True,
        )
        print(
            f"[Original] Details: {state / 'dependency-report.json'}; rerun Start-Portable.ps1 -Repair to retry.",
            flush=True,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
