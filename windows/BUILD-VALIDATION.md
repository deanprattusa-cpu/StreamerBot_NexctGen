# Verified continuation build — 2026-10-29

Installer source revision: `c1348123148734ad8864aef788bd9985e2f6ffd6`.

- [Windows run 40845803387](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40845803387): completed successfully.
- [Download StreamerBot-Native-Windows-x64](https://github.com/deanprattusa-cpu/StreamerBot_NexctGen/actions/runs/40845803387/artifacts/12885555698).
- Extract the artifact ZIP and run `StreamerBot-Native-Setup.exe` on Windows 11 x64.
- Installer size: 64,910,614 bytes. Artifact ZIP: 65,019,573 bytes.
- Installer SHA-256: `c8a87d0e81302e3a02f238ef4f606b1863f38d3c9b4529202e1e291095db0e93`.

## Checks observed in this continuation

On Linux: 28 C# integration checks, 5 Python worker tests and 6 Python video/path
tests passed with .NET SDK 10.0.403/runtime 10.0.14 and Python 3.14.4.

On the Windows CI runner: the same 28 + 5 + 6 checks passed, full self-contained
Windows publish completed, the native window loaded and laid out, and the
installer was built. Actual silent installation, installed-app launch and
uninstall preserving a user-added file and synthetic profile passed.

Downloaded artifact: installer checksum matched `SHA256SUMS.txt`. Published and
installed app smoke folders both contain `smoke-ready.txt`, `paths-ready.txt`,
`native-preview.png` and a synthetic profile. The installed-app screenshot was
visually inspected. Published smoke stderr is empty. Paths markers confirm the
dialog loaded all resource fields; this does not verify interactive file picking.

The original control-panel repair ZIP is unchanged. Its backup SHA-256, ZIP CRC
and all seven payload hashes were verified using the repair script's newline
normalization. This turn did not apply that repair on a user's Windows PC.

## Windows PC setup still required

Open Tools → Paths and select the SadTalker project containing `inference.py`,
its GPU-enabled virtual-environment `Scripts/python.exe`, avatar, optional
checkpoints, FFmpeg and video output. Enable avatar rendering. If it fails, use
`renderer.log` in the selected video output folder to diagnose the actual error.

No real NVIDIA GPU, SadTalker models, audio/microphone devices or live provider
accounts were tested here. Synthetic inference confirms process/path/error
handling. It does not establish real CUDA/model readiness or full original
behavioral parity. See [NATIVE-PARITY.md](NATIVE-PARITY.md) for remaining migration.
