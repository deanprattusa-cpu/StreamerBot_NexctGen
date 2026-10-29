"""Smart dependency installer for StreamerBot.

Why this exists: the old run.bat ran `pip install` for ALL ~55 packages on EVERY launch with the output
hidden, so it looked frozen (and packages that need compiling, like insightface / fairseq, were rebuilt and
retried every single time). This script:
  * checks what is ALREADY installed (instant) and only installs what is missing
  * shows live progress so you can see it is working
  * gives every package a time limit so one stuck build can't block the app
  * remembers packages that failed, and does not retry them each launch (use --retry to try again)
  * never stops the launch: the app still starts even if an optional package could not be installed
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import time
from importlib import metadata

try:
    from packaging.requirements import Requirement
except Exception:                       # very old environments: fall back to presence-only checks
    Requirement = None

HERE = os.path.dirname(os.path.abspath(__file__))
REQ_FILE = os.path.join(HERE, "requirements.txt")
STATE_FILE = os.path.join(HERE, ".deps_state.json")
CONSTRAINTS = os.path.join(HERE, "constraints.txt")
TIMEOUT_SECONDS = int(os.environ.get("SB_PIP_TIMEOUT", "900"))        # 15 min per package
SLOW_HINT = {
    "insightface": "needs 'Microsoft C++ Build Tools' to compile; can take 10+ minutes or fail without them",
    "fairseq": "compiles from source on Windows; can take 10+ minutes or fail",
    "basicsr": "large, compiles extensions; can take several minutes",
    "rvc-python": "pulls many dependencies; can take several minutes",
    "mediapipe": "large download",
    "kokoro": "pulls model dependencies",
    "gfpgan": "pulls many dependencies",
}


def parse_requirements(path):
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.split("#")[0].strip()
            if not line:
                continue
            if Requirement is not None:
                try:
                    r = Requirement(line)
                    out.append((r.name, line, r))
                    continue
                except Exception:
                    pass
            out.append((re.split(r"[<>=!~ ;\[]", line)[0], line, None))
    return out


def is_satisfied(name, req):
    try:
        version = metadata.version(name)
    except metadata.PackageNotFoundError:
        return False
    if req is not None and req.specifier:
        return req.specifier.contains(version, prereleases=True)
    return True


def load_state():
    try:
        with open(STATE_FILE, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {"failed": {}}


def save_state(state):
    try:
        with open(STATE_FILE, "w", encoding="utf-8") as f:
            json.dump(state, f, indent=1)
    except OSError:
        pass


def pip(args, timeout=None):
    cmd = [sys.executable, "-m", "pip"] + args
    return subprocess.run(cmd, timeout=timeout)


def install_one(spec):
    args = ["install", "--user", "--prefer-binary", "--disable-pip-version-check", "--no-input",
            "--default-timeout", "30", "--retries", "2", spec]
    if os.path.exists(CONSTRAINTS):
        args += ["-c", CONSTRAINTS]
    try:
        r = pip(args, timeout=TIMEOUT_SECONDS)
        return r.returncode == 0, ("pip returned an error" if r.returncode else "")
    except subprocess.TimeoutExpired:
        return False, f"timed out after {TIMEOUT_SECONDS // 60} min"
    except Exception as e:
        return False, str(e)


def main():
    retry = "--retry" in sys.argv
    reqs = parse_requirements(REQ_FILE)
    state = load_state()
    req_hash = hashlib.sha1(open(REQ_FILE, "rb").read()).hexdigest()
    if state.get("hash") != req_hash:           # requirements changed: give failed packages another chance
        state["failed"] = {}
    if retry:
        state["failed"] = {}

    missing = [(n, spec) for n, spec, r in reqs if not is_satisfied(n, r)]
    skipped = [(n, spec) for n, spec in missing if n.lower() in state["failed"]]
    todo = [(n, spec) for n, spec in missing if n.lower() not in state["failed"]]

    print(f"[Deps] {len(reqs) - len(missing)}/{len(reqs)} packages already installed.")
    if skipped:
        print(f"[Deps] Skipping {len(skipped)} package(s) that failed before (run 'python setup_deps.py --retry' to try again): "
              + ", ".join(n for n, _ in skipped))
    if not todo:
        state["hash"] = req_hash
        save_state(state)
        print("[Deps] Nothing to install. Starting.")
        return 0

    print(f"[Deps] Installing {len(todo)} missing package(s). You will see pip's progress below, it is not frozen.\n")
    # old pip is needed for some legacy packages (fairseq/omegaconf metadata); only touch pip when we must install
    pip(["install", "--user", "--disable-pip-version-check", "--no-input", "setuptools", "wheel", "pip<24.1"], timeout=300)

    failed_now = []
    for i, (name, spec) in enumerate(todo, 1):
        hint = SLOW_HINT.get(name.lower(), "")
        print(f"\n=== [{i}/{len(todo)}] {spec} {('(' + hint + ')') if hint else ''}", flush=True)
        started = time.time()
        ok, why = install_one(spec)
        took = time.time() - started
        if ok:
            print(f"[Deps] OK: {name} ({took:.0f}s)")
        else:
            print(f"[Deps] SKIPPED: {name} ({why}, {took:.0f}s). The app will still start; features that need it may be unavailable.")
            state["failed"][name.lower()] = {"reason": why, "when": time.strftime("%Y-%m-%d %H:%M")}
            failed_now.append(name)
        state["hash"] = req_hash
        save_state(state)                         # saved after every package so Ctrl+C never loses progress

    print(f"\n[Deps] Done. Installed {len(todo) - len(failed_now)}, skipped {len(failed_now)}.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\n[Deps] Interrupted. Progress is saved; starting the app anyway.")
        sys.exit(0)
