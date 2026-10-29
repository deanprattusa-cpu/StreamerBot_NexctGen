"""User-selected resource locations, shared by the original UI and video bridge."""
from __future__ import annotations
import json
import os
from pathlib import Path
import tempfile

ROOT = Path(__file__).resolve().parent
CATALOG = json.loads((ROOT / "resource-paths.json").read_text(encoding="utf-8"))
KEYS = {entry["key"] for entry in CATALOG}
SETTINGS_FILE = ROOT / "resource_paths.json"


def load_settings():
    values = {}
    profile = ROOT / "bot_config.json"
    if profile.is_file():
        try:
            document = json.loads(profile.read_text(encoding="utf-8-sig"))
            values.update({k: v for k, v in document.get("resource_paths", {}).items() if k in KEYS and isinstance(v, str)})
            values.update({k: v for k, v in document.items() if k in KEYS and isinstance(v, str) and v})
        except (ValueError, OSError, AttributeError):
            pass
    if SETTINGS_FILE.is_file():
        # A damaged paths file is reported; never silently replace user selections.
        selected = json.loads(SETTINGS_FILE.read_text(encoding="utf-8-sig"))
        if not isinstance(selected, dict):
            raise ValueError("resource_paths.json must contain a JSON object.")
        values.update({k: v for k, v in selected.items() if k in KEYS and isinstance(v, str)})
    return values


def path(key, fallback="", settings=None):
    values = load_settings() if settings is None else settings
    value = values.get(key) or fallback
    if not value:
        return ""
    selected = Path(os.path.expandvars(os.path.expanduser(value)))
    return str((selected if selected.is_absolute() else ROOT / selected).resolve())


def validate(values):
    errors = []
    for entry in CATALOG:
        value = values.get(entry["key"], "").strip()
        if not value:
            continue
        selected = Path(path(entry["key"], settings=values))
        if entry["kind"] == "file" and not selected.is_file():
            errors.append(entry["label"] + ": file not found")
        elif entry["kind"] == "folder" and not selected.is_dir():
            errors.append(entry["label"] + ": folder not found")
        elif entry["kind"] == "output" and selected.exists() and not selected.is_dir():
            errors.append(entry["label"] + ": select a folder")
        elif entry["key"] == "sadtalker_path" and not (selected / "inference.py").is_file():
            errors.append("SadTalker folder must contain inference.py")
    return errors


def save_settings(values):
    selected = {k: str(v).strip() for k, v in values.items() if k in KEYS}
    errors = validate(selected)
    if errors:
        raise ValueError("\n".join(errors))
    # Keep unrelated future fields while updating only this dialog's fields.
    previous = json.loads(SETTINGS_FILE.read_text(encoding="utf-8-sig")) if SETTINGS_FILE.exists() else {}
    if not isinstance(previous, dict):
        raise ValueError("resource_paths.json must contain a JSON object.")
    previous.update(selected)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=SETTINGS_FILE.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(previous, stream, indent=2)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, SETTINGS_FILE)
    finally:
        if temporary and temporary.exists():
            temporary.unlink()


def open_dialog(parent, on_saved=None):
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk
    window = tk.Toplevel(parent)
    window.title("Resource paths — models, voices and video")
    window.geometry("900x650")
    ttk.Label(window, text="Select existing resources on any drive. Blank uses automatic defaults. Output folders may be created when used.\nChanging a loaded model in the original app can require restarting it.", wraplength=850).pack(padx=15, pady=12, anchor="w")
    canvas = tk.Canvas(window, highlightthickness=0)
    scroll = ttk.Scrollbar(window, orient="vertical", command=canvas.yview)
    canvas.configure(yscrollcommand=scroll.set)
    scroll.pack(side="right", fill="y")
    canvas.pack(fill="both", expand=True)
    rows = ttk.Frame(canvas)
    item = canvas.create_window((0, 0), window=rows, anchor="nw")
    rows.bind("<Configure>", lambda _: canvas.configure(scrollregion=canvas.bbox("all")))
    canvas.bind("<Configure>", lambda e: canvas.itemconfigure(item, width=e.width))
    current = load_settings()
    variables = {}
    for row, entry in enumerate(e for e in CATALOG if not e.get("nativeOnly")):
        key = entry["key"]
        value = variables[key] = tk.StringVar(value=current.get(key, ""))
        ttk.Label(rows, text=entry["label"]).grid(row=row * 2, column=0, columnspan=2, sticky="w", padx=15, pady=(10, 2))
        ttk.Entry(rows, textvariable=value).grid(row=row * 2 + 1, column=0, sticky="ew", padx=(15, 5))
        def browse(field=value, descriptor=entry):
            selected = filedialog.askopenfilename(parent=window) if descriptor["kind"] == "file" else filedialog.askdirectory(parent=window)
            if selected:
                field.set(selected)
        ttk.Button(rows, text="Browse…", command=browse).grid(row=row * 2 + 1, column=1, padx=(0, 15))
    rows.columnconfigure(0, weight=1)
    def save():
        try:
            values = {k: v.get() for k, v in variables.items()}
            save_settings(values)
            if on_saved:
                on_saved(values)
            messagebox.showinfo("Resource paths", "Paths saved. Existing files and models were not moved.", parent=window)
            window.destroy()
        except (ValueError, OSError) as error:
            messagebox.showerror("Resource paths", str(error), parent=window)
    ttk.Button(window, text="Save paths", command=save).pack(pady=12)
    return window
