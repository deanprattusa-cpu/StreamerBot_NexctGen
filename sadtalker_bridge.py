"""Run a selected SadTalker installation in its own Python environment."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid
import resource_paths


class SadTalkerEngine:
    def __init__(self, sadtalker_path=None, python_path=None, settings=None):
        self.settings = resource_paths.load_settings() if settings is None else settings
        self.sadtalker_path = resource_paths.path("sadtalker_path", sadtalker_path or r"D:\BARKAIMODELS\SadTalker", self.settings) if sadtalker_path is None else str(Path(sadtalker_path).resolve())
        self.inference_script = str(Path(self.sadtalker_path) / "inference.py")
        explicit_python = python_path or self.settings.get("video_python_path")
        if explicit_python:
            self.python = resource_paths.path("video_python_path", explicit_python, {"video_python_path": explicit_python})
        else:
            project = Path(self.sadtalker_path)
            candidates = [project / name / part for name in ("venv", ".venv", "env") for part in ("Scripts/python.exe", "bin/python")]
            self.python = str(next((p for p in candidates if p.is_file()), Path(sys.executable)))
        self.last_error = ""
        self.log_path = ""

    def generate_talking_head(self, audio_path, image_path, output_dir=None, require_cuda=True, timeout=1200):
        self.last_error = ""
        self.log_path = ""
        project = Path(self.sadtalker_path)
        audio, image = Path(audio_path).resolve(), Path(image_path).resolve()
        output = Path(output_dir or resource_paths.path("video_output_path", "outputs", self.settings)).resolve()
        try:
            for required, label in ((Path(self.inference_script), "SadTalker inference.py"), (Path(self.python), "SadTalker Python executable"), (audio, "Driven audio"), (image, "Avatar image")):
                if not required.is_file():
                    raise ValueError(f"{label} not found: {required}. Select it in Tools → Paths.")
            job = output / ("render-" + uuid.uuid4().hex)
            job.mkdir(parents=True)
            log = job / "renderer.log"
            self.log_path = str(log)
            env = os.environ.copy()
            env["PYTHONUTF8"] = "1"
            ffmpeg = resource_paths.path("ffmpeg_path", settings=self.settings)
            if ffmpeg:
                if not Path(ffmpeg).is_file():
                    raise ValueError("Selected FFmpeg executable does not exist. Update Tools → Paths.")
            elif not shutil.which("ffmpeg"):
                try:
                    import imageio_ffmpeg
                    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
                except ImportError:
                    pass
            if ffmpeg:
                # Expose versioned bundled binaries as the literal 'ffmpeg' name.
                # Reuse an owned copy across jobs rather than copying it per phrase.
                ffmpeg_file = Path(ffmpeg)
                if ffmpeg_file.name.lower() in {"ffmpeg.exe", "ffmpeg"}:
                    tool_dir = ffmpeg_file.parent
                else:
                    identity = str(ffmpeg_file) + str(ffmpeg_file.stat().st_mtime_ns) + str(ffmpeg_file.stat().st_size)
                    tool_dir = output / ".tools" / hashlib.sha256(identity.encode()).hexdigest()[:16]
                    tool_dir.mkdir(parents=True, exist_ok=True)
                    target = tool_dir / ("ffmpeg.exe" if os.name == "nt" else "ffmpeg")
                    if not target.exists():
                        temporary = tool_dir / (uuid.uuid4().hex + ".tmp")
                        try:
                            shutil.copy2(ffmpeg_file, temporary)
                            if not target.exists():
                                os.replace(temporary, target)
                        finally:
                            if temporary.exists():
                                temporary.unlink()
                env["PATH"] = str(tool_dir) + os.pathsep + env.get("PATH", "")
            command = [self.python, self.inference_script, "--driven_audio", str(audio), "--source_image", str(image), "--result_dir", str(job), "--still", "--preprocess", "full", "--batch_size", "8"]
            checkpoints = resource_paths.path("video_checkpoints_path", settings=self.settings)
            if checkpoints:
                if not Path(checkpoints).is_dir():
                    raise ValueError("Selected SadTalker checkpoints folder does not exist.")
                command += ["--checkpoint_dir", checkpoints]
            if not require_cuda:
                command += ["--cpu"]
            started = time.monotonic()
            with log.open("w", encoding="utf-8") as stream:
                stream.write(f"Python: {self.python}\nProject: {project}\n\n")
                stream.flush()
                if require_cuda:
                    probe = "import torch; assert torch.cuda.is_available(), 'CUDA is unavailable in selected SadTalker Python'; x=torch.ones((8,8),device='cuda'); assert (x@x).sum().item()==512; torch.cuda.synchronize(); print('CUDA tensor probe passed', flush=True)"
                    checked = subprocess.run([self.python, "-c", probe], cwd=project, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=90)
                    if checked.returncode:
                        raise RuntimeError("CUDA check failed in the selected SadTalker environment. Select its GPU-enabled Python executable, not a CPU-only/system Python.")
                print(f"[SadTalker] Rendering with {self.python}; detailed log: {log}")
                rendered = subprocess.run(command, cwd=project, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout)
                if rendered.returncode:
                    raise RuntimeError(f"SadTalker exited with code {rendered.returncode}.")
            video = self._get_latest_mp4(job)
            if not video:
                raise RuntimeError("SadTalker exited successfully but produced no video in this render's output folder.")
            print(f"[SadTalker] Video rendered in {time.monotonic()-started:.1f}s: {video}")
            return video
        except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
            self.last_error = str(error)
            detail = ""
            if self.log_path and Path(self.log_path).is_file():
                with Path(self.log_path).open("rb") as stream:
                    stream.seek(max(0, Path(self.log_path).stat().st_size - 6000))
                    detail = stream.read().decode("utf-8", errors="replace")
            print(f"[SadTalker Error] {error}\n{detail}\nFull renderer log: {self.log_path or 'not started'}")
            return None

    def _get_latest_mp4(self, directory):
        # Only the current job is searched; never return an older unrelated video.
        files = [p for p in Path(directory).rglob("*") if p.is_file() and p.suffix.lower() == ".mp4" and p.stat().st_size > 0]
        return str(max(files, key=lambda p: p.stat().st_mtime)) if files else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", required=True)
    parser.add_argument("--audio", required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--response", required=True)
    parser.add_argument("--checkpoints", default="")
    parser.add_argument("--ffmpeg", default="")
    parser.add_argument("--cpu", action="store_true")
    args = parser.parse_args()
    engine = SadTalkerEngine(args.project, sys.executable, {"video_checkpoints_path": args.checkpoints, "ffmpeg_path": args.ffmpeg})
    video = engine.generate_talking_head(args.audio, args.image, args.output, require_cuda=not args.cpu)
    Path(args.response).write_text(json.dumps({"video": video, "log": engine.log_path, "error": engine.last_error}), encoding="utf-8")
    return 0 if video else 1


if __name__ == "__main__":
    raise SystemExit(main())
