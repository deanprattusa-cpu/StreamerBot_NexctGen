"""Synthetic resource/path and renderer-process regression checks; no real GPU/models."""
from pathlib import Path
import contextlib
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
import resource_paths
from sadtalker_bridge import SadTalkerEngine


class VideoPathChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="streamerbot paths ")
        self.root = Path(self.temp.name).resolve()
        self.project = self.root / "SadTalker project"
        self.project.mkdir()
        (self.project / "inference.py").write_text("# synthetic")
        self.python = self.project / "venv/Scripts/python.exe"
        self.python.parent.mkdir(parents=True)
        self.python.write_bytes(b"synthetic")
        self.audio = self.root / "voice input.wav"; self.audio.write_bytes(b"synthetic")
        self.image = self.root / "avatar image.png"; self.image.write_bytes(b"synthetic")
        self.output = self.root / "video output"; self.output.mkdir()
        (self.output / "old.mp4").write_bytes(b"old unrelated video")

    def tearDown(self):
        self.temp.cleanup()

    def test_save_paths_preserves_unknown_fields_and_rejects_missing_inputs(self):
        selected = self.root / "paths.json"
        selected.write_text('{"future_field":{"retained":true}}')
        with patch.object(resource_paths, "ROOT", self.root), patch.object(resource_paths, "SETTINGS_FILE", selected):
            resource_paths.save_settings({"sadtalker_path":str(self.project),"video_output_path":str(self.root / "new folder")})
            self.assertEqual(resource_paths.load_settings()["sadtalker_path"], str(self.project))
            self.assertTrue(json.loads(selected.read_text())["future_field"]["retained"])
            before = selected.read_bytes()
            with self.assertRaises(ValueError): resource_paths.save_settings({"avatar_path":str(self.root / "missing.png")})
            self.assertEqual(before,selected.read_bytes())

    def test_project_venv_spaces_checkpoints_ffmpeg_and_current_job_output(self):
        checkpoints = self.root / "checkpoints on selected drive"; checkpoints.mkdir()
        ffmpeg = self.root / "ffmpeg-versioned.exe"; ffmpeg.write_bytes(b"synthetic ffmpeg")
        calls=[]
        def run(args,**options):
            calls.append((args,options))
            if "--result_dir" in args:
                job = Path(args[args.index("--result_dir")+1]); (job / "new.mp4").write_bytes(b"new video")
            return types.SimpleNamespace(returncode=0)
        settings={"video_checkpoints_path":str(checkpoints),"ffmpeg_path":str(ffmpeg)}
        engine=SadTalkerEngine(str(self.project),settings=settings)
        with patch("sadtalker_bridge.subprocess.run",side_effect=run), contextlib.redirect_stdout(io.StringIO()):
            video=engine.generate_talking_head(str(self.audio),str(self.image),str(self.output))
        self.assertEqual(str(self.python),engine.python)
        self.assertEqual(calls[0][0][0],str(self.python))
        args, options=calls[1]
        self.assertEqual(args[args.index("--source_image")+1],str(self.image))
        self.assertEqual(args[args.index("--checkpoint_dir")+1],str(checkpoints))
        self.assertEqual(options["cwd"],self.project)
        tool=Path(options["env"]["PATH"].split(__import__('os').pathsep)[0])
        self.assertEqual((tool / ("ffmpeg.exe" if __import__('os').name=='nt' else 'ffmpeg')).read_bytes(),ffmpeg.read_bytes())
        self.assertEqual(Path(video).read_bytes(),b"new video")
        self.assertNotEqual(Path(video),self.output / "old.mp4")

    def test_failed_cuda_probe_does_not_run_inference(self):
        engine=SadTalkerEngine(str(self.project),settings={})
        with patch("sadtalker_bridge.subprocess.run",return_value=types.SimpleNamespace(returncode=1)) as run, contextlib.redirect_stdout(io.StringIO()):
            self.assertIsNone(engine.generate_talking_head(str(self.audio),str(self.image),str(self.output)))
        self.assertEqual(run.call_count,1)
        self.assertIn("selected SadTalker environment",engine.last_error)

    def test_inference_error_retains_real_stderr_in_log(self):
        def fail(args,**options):
            options['stdout'].write('ModuleNotFoundError: synthetic_missing_dependency\n');options['stdout'].flush()
            return types.SimpleNamespace(returncode=1)
        engine=SadTalkerEngine(str(self.project),settings={})
        capture=io.StringIO()
        with patch("sadtalker_bridge.subprocess.run",side_effect=fail),contextlib.redirect_stdout(capture):
            self.assertIsNone(engine.generate_talking_head(str(self.audio),str(self.image),str(self.output),require_cuda=False))
        self.assertIn('synthetic_missing_dependency',Path(engine.log_path).read_text())
        self.assertIn('synthetic_missing_dependency',capture.getvalue())
        self.assertIn('code 1',engine.last_error)

    def test_success_without_new_video_does_not_reuse_old_video(self):
        engine=SadTalkerEngine(str(self.project),settings={})
        with patch("sadtalker_bridge.subprocess.run",return_value=types.SimpleNamespace(returncode=0)),contextlib.redirect_stdout(io.StringIO()):
            self.assertIsNone(engine.generate_talking_head(str(self.audio),str(self.image),str(self.output),require_cuda=False))
        self.assertIn('produced no video',engine.last_error)

    def test_missing_image_fails_before_process(self):
        engine=SadTalkerEngine(str(self.project),settings={})
        with patch("sadtalker_bridge.subprocess.run") as run, contextlib.redirect_stdout(io.StringIO()):
            self.assertIsNone(engine.generate_talking_head(str(self.audio),str(self.root/'missing.png'),str(self.output)))
        run.assert_not_called()
        self.assertIn('Tools → Paths',engine.last_error)

if __name__=='__main__': unittest.main()
