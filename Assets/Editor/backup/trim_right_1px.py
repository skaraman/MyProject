from pathlib import Path
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from PIL import Image

try:
    from tkinterdnd2 import DND_FILES, TkinterDnD
except ImportError:
    DND_FILES = None
    TkinterDnD = None


def trim_right_edge(path_str: str) -> str:
    path = Path(path_str)
    if not path.is_file():
        return f"SKIP missing file: {path}"

    with Image.open(path) as image:
        width, height = image.size
        if width <= 1:
            return f"SKIP too narrow: {path} ({width}x{height})"

        trimmed = image.crop((0, 0, width - 1, height))
        trimmed.save(path)
        return f"TRIM {path} from {width}x{height} to {width - 1}x{height}"


class TrimRightEdgeApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("Trim Right 1px")
        self.paths: list[str] = []

        frame = ttk.Frame(root, padding=12)
        frame.pack(fill="both", expand=True)

        ttk.Label(
            frame,
            text="Drop image files into the box or use Add Files.",
        ).pack(anchor="w")

        self.drop_box = tk.Text(frame, height=10, width=72, wrap="word")
        self.drop_box.pack(fill="both", expand=True, pady=(8, 8))
        self.drop_box.insert("1.0", "Drop files here\n")
        self.drop_box.configure(state="disabled")

        if TkinterDnD is not None and DND_FILES is not None:
            self.drop_box.drop_target_register(DND_FILES)
            self.drop_box.dnd_bind("<<Drop>>", self.on_drop)
        else:
            ttk.Label(
                frame,
                text="Drag-drop needs tkinterdnd2. Install with: pip install tkinterdnd2",
            ).pack(anchor="w")

        buttons = ttk.Frame(frame)
        buttons.pack(fill="x", pady=(0, 8))

        ttk.Button(buttons, text="Add Files", command=self.add_files).pack(side="left")
        ttk.Button(buttons, text="Clear", command=self.clear_files).pack(side="left", padx=(8, 0))
        ttk.Button(buttons, text="Trim Files", command=self.trim_files).pack(side="right")

        self.status_var = tk.StringVar(value="Ready")
        ttk.Label(frame, textvariable=self.status_var).pack(anchor="w")

    def on_drop(self, event) -> None:
        for path in self.root.tk.splitlist(event.data):
            self.add_path(path)

    def add_files(self) -> None:
        file_paths = filedialog.askopenfilenames(
            title="Select image files",
            filetypes=[("Image files", "*.png *.jpg *.jpeg *.bmp *.tga *.webp"), ("All files", "*.*")],
        )
        for path in file_paths:
            self.add_path(path)

    def add_path(self, path: str) -> None:
        normalized = str(Path(path))
        if normalized in self.paths:
            return
        self.paths.append(normalized)
        self.refresh_drop_box()

    def clear_files(self) -> None:
        self.paths.clear()
        self.refresh_drop_box()
        self.status_var.set("Ready")

    def refresh_drop_box(self) -> None:
        self.drop_box.configure(state="normal")
        self.drop_box.delete("1.0", "end")
        if not self.paths:
            self.drop_box.insert("1.0", "Drop files here\n")
        else:
            self.drop_box.insert("1.0", "\n".join(self.paths))
        self.drop_box.configure(state="disabled")

    def trim_files(self) -> None:
        if not self.paths:
            messagebox.showerror("No Files", "Add or drop at least one image file.")
            return

        results = [trim_right_edge(path) for path in self.paths]
        trimmed_count = sum(1 for line in results if line.startswith("TRIM "))
        self.status_var.set(f"Done. Trimmed {trimmed_count} file(s).")
        messagebox.showinfo("Trim Complete", "\n".join(results[:20]))


def main() -> int:
    root_class = TkinterDnD.Tk if TkinterDnD is not None else tk.Tk
    root = root_class()
    TrimRightEdgeApp(root)
    root.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
