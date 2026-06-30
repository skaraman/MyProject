import argparse
import json
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path


DEFAULT_BACKUP_SUFFIX = ".offset-cleanup.bak"
DEFAULT_UI_ROOT = "Assets/Sprites"


@dataclass
class FileStats:
    sprites_seen: int = 0
    sprites_written: int = 0
    skipped_sprites: int = 0
    missing_offsets: int = 0
    changed: bool = False


@dataclass
class CleanupSummary:
    root: Path
    mode: str
    backup_suffix: str
    json_files_scanned: int = 0
    candidate_files: int = 0
    changed_files: int = 0
    skipped_files: int = 0
    errors: int = 0
    sprites_in: int = 0
    sprites_out: int = 0
    missing_offsets: int = 0


def parse_args(argv=None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Walk a folder tree, find supported sprite metadata JSON files, and keep only sprite names plus offset x/y."
    )
    parser.add_argument("root", nargs="?", help="Folder to scan recursively. Omit to open the UI.")
    parser.add_argument(
        "--write",
        action="store_true",
        help="Rewrite files in place. Without this flag the script only reports what would change.",
    )
    parser.add_argument(
        "--backup-suffix",
        default=DEFAULT_BACKUP_SUFFIX,
        help="Backup suffix to write before overwriting a file. Use an empty string to disable backups.",
    )
    parser.add_argument(
        "--gui",
        action="store_true",
        help="Open the folder-picker UI even if a root path is provided.",
    )
    return parser.parse_args(argv)


def resolve_root(root_value: str) -> Path:
    return Path(root_value).expanduser().resolve()


def iter_json_paths(root: Path):
    for path in sorted(root.rglob("*.json")):
        if path.is_file():
            yield path


def try_get_float(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def extract_offset(sprite_data: dict) -> tuple[dict, bool]:
    offset_data = sprite_data.get("offsetFromCellCenterPx")
    if not isinstance(offset_data, dict):
        return {"x": 0.0, "y": 0.0}, True

    x = try_get_float(offset_data.get("x"))
    y = try_get_float(offset_data.get("y"))
    missing_offset = x is None or y is None
    return {
        "x": 0.0 if x is None else x,
        "y": 0.0 if y is None else y,
    }, missing_offset


def validate_candidate_payload(raw_data) -> list:
    if not isinstance(raw_data, dict):
        raise ValueError("top-level JSON value is not an object")

    sprites = raw_data.get("sprites")
    if not isinstance(sprites, list):
        raise ValueError("missing sprites array")
    return sprites


def build_offset_only_payload(raw_data) -> tuple[dict, FileStats]:
    sprites = validate_candidate_payload(raw_data)

    stats = FileStats()
    cleaned_sprites = []

    for sprite_data in sprites:
        stats.sprites_seen += 1
        if not isinstance(sprite_data, dict):
            stats.skipped_sprites += 1
            continue

        sprite_name = str(sprite_data.get("name") or "").strip()
        if not sprite_name:
            stats.skipped_sprites += 1
            continue

        offset_data, missing_offset = extract_offset(sprite_data)
        if missing_offset:
            stats.missing_offsets += 1

        cleaned_sprites.append(
            {
                "name": sprite_name,
                "offsetFromCellCenterPx": offset_data,
            }
        )
        stats.sprites_written += 1

    cleaned_payload = {"sprites": cleaned_sprites}
    stats.changed = cleaned_payload != raw_data
    return cleaned_payload, stats


def format_json(payload: dict) -> str:
    return json.dumps(payload, indent=4, ensure_ascii=True) + "\n"


def write_backup(path: Path, backup_suffix: str) -> None:
    if not backup_suffix:
        return

    backup_path = path.with_name(path.name + backup_suffix)
    shutil.copyfile(path, backup_path)


def process_file(path: Path, write_changes: bool, backup_suffix: str) -> tuple[bool, FileStats]:
    with path.open("r", encoding="utf-8") as handle:
        raw_data = json.load(handle)

    cleaned_payload, stats = build_offset_only_payload(raw_data)
    if not stats.changed or not write_changes:
        return stats.changed, stats

    write_backup(path, backup_suffix)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(format_json(cleaned_payload))

    return True, stats


def emit_message(emit, message: str) -> None:
    if emit is not None:
        emit(message)


def create_summary(root: Path, write_changes: bool, backup_suffix: str) -> CleanupSummary:
    return CleanupSummary(
        root=root,
        mode="write" if write_changes else "dry-run",
        backup_suffix=backup_suffix,
    )


def format_summary(summary: CleanupSummary) -> str:
    return (
        f"[atlas-offset-cleanup] done mode='{summary.mode}' json_files_scanned={summary.json_files_scanned} "
        f"candidate_files={summary.candidate_files} changed_files={summary.changed_files} skipped_files={summary.skipped_files} "
        f"errors={summary.errors} sprites_in={summary.sprites_in} sprites_out={summary.sprites_out} "
        f"missing_offsets={summary.missing_offsets}"
    )


def run_cleanup(root: Path, write_changes: bool, backup_suffix: str, emit=None) -> CleanupSummary:
    summary = create_summary(root, write_changes, backup_suffix)
    json_paths = list(iter_json_paths(root))
    summary.json_files_scanned = len(json_paths)

    if not json_paths:
        emit_message(emit, f"[atlas-offset-cleanup] no .json files found under '{root}'")
        return summary

    emit_message(
        emit,
        f"[atlas-offset-cleanup] start mode='{summary.mode}' root='{root}' json_files_scanned={summary.json_files_scanned} backup_suffix='{backup_suffix}'",
    )

    for path in json_paths:
        try:
            with path.open("r", encoding="utf-8") as handle:
                raw_data = json.load(handle)
            validate_candidate_payload(raw_data)
        except Exception as exc:
            summary.skipped_files += 1
            emit_message(emit, f"[atlas-offset-cleanup] skip path='{path}' reason='{exc}'")
            continue

        try:
            changed, stats = process_file(path, write_changes, backup_suffix)
        except Exception as exc:
            summary.errors += 1
            emit_message(emit, f"[atlas-offset-cleanup] error path='{path}' message='{exc}'")
            continue

        summary.candidate_files += 1
        summary.sprites_in += stats.sprites_seen
        summary.sprites_out += stats.sprites_written
        summary.missing_offsets += stats.missing_offsets
        if changed:
            summary.changed_files += 1

        emit_message(
            emit,
            f"[atlas-offset-cleanup] file='{path}' changed={changed} sprites_in={stats.sprites_seen} "
            f"sprites_out={stats.sprites_written} skipped={stats.skipped_sprites} missing_offsets={stats.missing_offsets}",
        )

    emit_message(emit, format_summary(summary))
    return summary


def build_default_ui_root() -> str:
    candidate = resolve_root(DEFAULT_UI_ROOT)
    return str(candidate if candidate.exists() else Path.cwd())


def launch_ui(initial_root: str, backup_suffix: str) -> int:
    try:
        import tkinter as tk
        from tkinter import filedialog, messagebox, scrolledtext
    except Exception as exc:
        print(f"[atlas-offset-cleanup] failed to start UI: {exc}", file=sys.stderr)
        return 1

    colors = {
        "bg": "#0f141b",
        "panel": "#161d26",
        "field": "#0b1016",
        "text": "#e6edf3",
        "muted": "#9aa7b7",
        "border": "#27313d",
        "button": "#243041",
        "button_hover": "#304158",
        "accent": "#2f81f7",
        "accent_hover": "#4a96ff",
    }

    window = tk.Tk()
    window.title("Atlas Offset Cleanup")
    window.geometry("1100x720")
    window.minsize(900, 600)
    window.configure(bg=colors["bg"])

    folder_var = tk.StringVar(value=initial_root)
    backup_var = tk.StringVar(value=backup_suffix)
    status_var = tk.StringVar(value="Choose a folder, then run dry-run or write.")

    def style_panel(frame) -> None:
        frame.configure(bg=colors["panel"], highlightbackground=colors["border"], highlightthickness=1)

    def style_label(label, muted: bool = False) -> None:
        label.configure(
            bg=label.master.cget("bg"),
            fg=colors["muted"] if muted else colors["text"],
        )

    def style_entry(entry) -> None:
        entry.configure(
            bg=colors["field"],
            fg=colors["text"],
            insertbackground=colors["text"],
            selectbackground=colors["accent"],
            selectforeground=colors["text"],
            relief=tk.FLAT,
            highlightbackground=colors["border"],
            highlightcolor=colors["accent"],
            highlightthickness=1,
            bd=0,
        )

    def style_button(button, accent: bool = False) -> None:
        base_color = colors["accent"] if accent else colors["button"]
        hover_color = colors["accent_hover"] if accent else colors["button_hover"]
        button.configure(
            bg=base_color,
            fg=colors["text"],
            activebackground=hover_color,
            activeforeground=colors["text"],
            relief=tk.FLAT,
            bd=0,
            highlightthickness=0,
            padx=12,
            pady=8,
            cursor="hand2",
        )

    def append_result(message: str) -> None:
        results_text.insert(tk.END, message + "\n")
        results_text.see(tk.END)
        window.update_idletasks()

    def resolve_selected_root() -> Path | None:
        root_text = folder_var.get().strip()
        if not root_text:
            messagebox.showerror("Atlas Offset Cleanup", "Choose a folder first.")
            return None

        root = resolve_root(root_text)
        if not root.exists() or not root.is_dir():
            messagebox.showerror("Atlas Offset Cleanup", f"Folder does not exist:\n{root}")
            return None

        folder_var.set(str(root))
        return root

    def browse_for_root() -> None:
        selected = filedialog.askdirectory(
            title="Choose Folder To Scan",
            initialdir=folder_var.get().strip() or build_default_ui_root(),
        )
        if selected:
            folder_var.set(str(resolve_root(selected)))

    def run_from_ui(write_changes: bool) -> None:
        root = resolve_selected_root()
        if root is None:
            return

        suffix = backup_var.get()
        if write_changes:
            confirmed = messagebox.askyesno(
                "Confirm Write",
                f"Rewrite all supported sprite metadata .json files under:\n{root}\n\nBackup suffix: {suffix or '(disabled)'}",
            )
            if not confirmed:
                return

        results_text.delete("1.0", tk.END)
        status_var.set(f"Running {('write' if write_changes else 'dry-run')}...")
        window.update_idletasks()

        summary = run_cleanup(root, write_changes, suffix, append_result)
        if summary.errors > 0:
            status_var.set(
                f"Finished with errors. changed_files={summary.changed_files} errors={summary.errors}"
            )
        else:
            status_var.set(
                f"Finished {summary.mode}. changed_files={summary.changed_files} candidate_files={summary.candidate_files}"
            )

    controls_frame = tk.Frame(window, padx=12, pady=12)
    style_panel(controls_frame)
    controls_frame.pack(fill=tk.X)

    folder_label = tk.Label(controls_frame, text="Folder")
    style_label(folder_label)
    folder_label.grid(row=0, column=0, sticky="w")
    folder_entry = tk.Entry(controls_frame, textvariable=folder_var)
    style_entry(folder_entry)
    folder_entry.grid(row=0, column=1, sticky="ew", padx=(8, 8))
    browse_button = tk.Button(controls_frame, text="Browse...", command=browse_for_root, width=12)
    style_button(browse_button)
    browse_button.grid(row=0, column=2)

    backup_label = tk.Label(controls_frame, text="Backup Suffix")
    style_label(backup_label)
    backup_label.grid(row=1, column=0, sticky="w", pady=(8, 0))
    backup_entry = tk.Entry(controls_frame, textvariable=backup_var)
    style_entry(backup_entry)
    backup_entry.grid(row=1, column=1, sticky="ew", padx=(8, 8), pady=(8, 0))

    actions_frame = tk.Frame(controls_frame)
    actions_frame.configure(bg=colors["panel"])
    actions_frame.grid(row=1, column=2, sticky="e", pady=(8, 0))
    dry_run_button = tk.Button(actions_frame, text="Dry Run", command=lambda: run_from_ui(False), width=12)
    style_button(dry_run_button)
    dry_run_button.pack(side=tk.LEFT)
    write_button = tk.Button(actions_frame, text="Write", command=lambda: run_from_ui(True), width=12)
    style_button(write_button, accent=True)
    write_button.pack(side=tk.LEFT, padx=(8, 0))

    controls_frame.grid_columnconfigure(1, weight=1)

    status_frame = tk.Frame(window, padx=12)
    style_panel(status_frame)
    status_frame.pack(fill=tk.X)
    status_label = tk.Label(status_frame, textvariable=status_var, anchor="w")
    style_label(status_label, muted=True)
    status_label.pack(fill=tk.X, pady=8)

    results_frame = tk.Frame(window, padx=12, pady=12)
    style_panel(results_frame)
    results_frame.pack(fill=tk.BOTH, expand=True)
    results_text = scrolledtext.ScrolledText(results_frame, wrap=tk.WORD)
    results_text.configure(
        bg=colors["field"],
        fg=colors["text"],
        insertbackground=colors["text"],
        selectbackground=colors["accent"],
        selectforeground=colors["text"],
        relief=tk.FLAT,
        highlightbackground=colors["border"],
        highlightcolor=colors["accent"],
        highlightthickness=1,
        bd=0,
    )
    results_text.pack(fill=tk.BOTH, expand=True)

    append_result("[atlas-offset-cleanup] UI ready")
    append_result("[atlas-offset-cleanup] dry-run previews changes without writing")
    append_result("[atlas-offset-cleanup] write rewrites supported sprite metadata .json files and creates backups when backup suffix is not empty")

    window.mainloop()
    return 0


def run_cli(args: argparse.Namespace) -> int:
    root = resolve_root(args.root)
    if not root.exists() or not root.is_dir():
        print(f"[atlas-offset-cleanup] invalid root='{root}'", file=sys.stderr)
        return 1

    summary = run_cleanup(root, args.write, args.backup_suffix, print)
    return 0 if summary.errors == 0 else 2


def main(argv=None) -> int:
    args = parse_args(argv)
    should_launch_ui = args.gui or not args.root
    if should_launch_ui:
        initial_root = build_default_ui_root() if not args.root else str(resolve_root(args.root))
        return launch_ui(initial_root, args.backup_suffix)

    return run_cli(args)


if __name__ == "__main__":
    raise SystemExit(main())
