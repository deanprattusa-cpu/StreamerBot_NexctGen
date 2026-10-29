"""Stage a published native app and an explicit, credential-free voice payload."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
VOICE_FILES = [
    "resource-paths.json", "resource_paths.py", "sadtalker_bridge.py", "Toggle_Video_Rendering.bat",
    "model.py", "bark_fast.py", "typecast_bridge.py", "elevenlabs_ghost_bridge.py",
    "setup_deps.py", "requirements.txt", "constraints.txt",
    "windows/voice/worker.py", "windows/Prepare-Voices.ps1", "windows/Portable-Runtime.ps1",
    "windows/portable-runtimes.json", "windows/prepare_original.py",
    "windows/original-core-lock.txt", "windows/original-constraints.txt",
]
REQUIRED = ["StreamerBot.Native.exe", "StreamerBot.Native.dll", "StreamerBot.Native.Core.dll",
            "StreamerBot.Native.runtimeconfig.json", "StreamerBot.Native.deps.json",
            "StreamerBot.Native.pri", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "Microsoft.UI.Xaml.dll", "Microsoft.UI.pri"]


def stage(published: Path, destination: Path):
    if destination.exists():
        raise ValueError("Use a fresh staging directory; existing files are never silently mixed.")
    for name in REQUIRED:
        if not (published / name).is_file():
            raise ValueError("Incomplete native publish: missing " + name)
    shutil.copytree(published, destination, ignore=shutil.ignore_patterns("*.pdb"))
    for name in VOICE_FILES:
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / name, target)
    shutil.copyfile(ROOT / "windows/NATIVE-PARITY.md", destination / "NATIVE-PARITY.md")
    excluded = {".env", "keys.txt", "bot_config.json", "sbv2_config.json", "ai_failover_state.json"}
    for path in destination.rglob("*"):
        if path.is_file() and (path.name in excluded or "credentials" in [p.lower() for p in path.relative_to(destination).parts] or path.name.startswith(("client_secret", "token_"))):
            raise ValueError("Unexpected private payload file: " + str(path.relative_to(destination)))
    inventory = {str(p.relative_to(destination)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in sorted(destination.rglob("*")) if p.is_file()}
    (destination / "PAYLOAD-SHA256.json").write_text(json.dumps(inventory, indent=2), encoding="utf-8")
    def nsis_path(path):
        return str(path).replace("\\", "/").replace("$", "$$").replace('"', '$\\"').replace("/", "\\")
    lines = ['Delete "$INSTDIR\\' + nsis_path(name) + '"' for name in inventory]
    lines += ['Delete "$INSTDIR\\PAYLOAD-SHA256.json"', 'Delete "$INSTDIR\\BUILD-VALIDATION.md"']
    directories = sorted((p.relative_to(destination) for p in destination.rglob("*") if p.is_dir()), key=lambda p: len(p.parts), reverse=True)
    lines += ['RMDir "$INSTDIR\\' + nsis_path(path) + '"' for path in directories]
    (destination.parent / "uninstall_payload.nsh").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return inventory


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("published", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    inventory = stage(args.published.resolve(), args.destination.resolve())
    print(f"Staged {len(inventory)} allowlisted / published files; no user profiles or credentials.")
