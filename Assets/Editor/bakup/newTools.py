import os
import shutil
import threading
import re
import zlib
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from PIL import Image

A = 2048
B = 4
EXT = {".png", ".jpg", ".jpeg"}
ATLAS = "atlas.png"
CPU_COUNT = os.cpu_count() or 4
IO_WORKERS = min(32, CPU_COUNT * 2)
IMG_WORKERS = min(32, CPU_COUNT)


def walk(root, exts):
    return iter_files(root, exts)


def iter_files(root, exts):
    exts = {e.lower() for e in exts}
    stack = [root]
    while stack:
        path = stack.pop()
        try:
            with os.scandir(path) as it:
                for entry in it:
                    if entry.is_dir(follow_symlinks=False):
                        stack.append(entry.path)
                    elif entry.is_file(follow_symlinks=False):
                        ext = os.path.splitext(entry.name)[1].lower()
                        if ext in exts:
                            yield entry.path
        except Exception:
            continue


def out(p, i, s):
    b, e = os.path.splitext(p)
    return p if i else b + (s or "") + e


class AA:
    def __init__(self, r):
        self.r = r
        r.title("Esperanza Tools Hub")
        r.geometry("700x620")
        self._img_workers = IMG_WORKERS
        self._io_workers = IO_WORKERS
        self._style()
        self.n = ttk.Notebook(r)
        self.n.pack(fill="both", expand=1, padx=10, pady=10)
        for f in (self._comp, self._desat, self._dup, self._crunch,
                  self._atlas, self._slices, self._spritelib,
                  self._spritelib_overwrite, self._spritelib_renumber,
                  self._rename_files):
            f()

    def _style(self):
        r = self.r
        r.configure(bg="#1e1e1e")
        r.option_add("*Background", "#1e1e1e")
        r.option_add("*Foreground", "#e6e6e6")
        st = ttk.Style(r)
        try:
            st.theme_use("clam")
        except Exception:
            pass
        st.configure(".", background="#1e1e1e", foreground="#e6e6e6")
        st.configure("TFrame", background="#1e1e1e")
        st.configure("TNotebook", background="#1e1e1e")
        st.configure("TNotebook.Tab", background="#2a2a2a",
                     foreground="#e6e6e6", padding=[10, 6])
        st.map("TNotebook.Tab", background=[("selected", "#3a3a3a")])
        st.configure("TEntry", fieldbackground="#2a2a2a", foreground="#e6e6e6")
        st.configure("TButton", background="#2a2a2a", foreground="#e6e6e6")
        st.configure("Horizontal.TProgressbar",
                     background="#4a4a4a", troughcolor="#2a2a2a")

    def _tab(self, t):
        f = ttk.Frame(self.n)
        self.n.add(f, text=t)
        f.grid_columnconfigure(1, weight=1)
        return f

    def _pick(self, v):
        p = filedialog.askdirectory()
        if p:
            v.set(p)

    def _run(self, start, work, end):
        def r():
            try:
                o = work()
                self.r.after(0, lambda: end(o))
            except Exception as e:
                self.r.after(0, lambda: messagebox.showerror("Error", str(e)))
        start()
        threading.Thread(target=r, daemon=1).start()

    def _parallel_for_each(self, items, func, workers):
        if not items:
            return
        if len(items) == 1:
            func(items[0])
            return
        with ThreadPoolExecutor(max_workers=workers) as ex:
            for _ in ex.map(func, items):
                pass

    def _parallel_map(self, items, func, workers):
        if not items:
            return []
        if len(items) == 1:
            return [func(items[0])]
        with ThreadPoolExecutor(max_workers=workers) as ex:
            return list(ex.map(func, items))

    def _comp(self):
        f = self._tab("10x10 Compositor")
        self.comp_folder = tk.StringVar(self.r)
        self.comp_prefix = tk.StringVar(self.r, "death 1_")
        self.comp_canvas = tk.StringVar(self.r, "1920")
        self.comp_grid = tk.StringVar(self.r, "10")
        self.comp_status = tk.StringVar(self.r, "Idle")
        for i, (t, v, w) in enumerate((("Root:", self.comp_folder, 50), ("Prefix:", self.comp_prefix, 25),
                                       ("Canvas:", self.comp_canvas, 10), ("Grid:", self.comp_grid, 10))):
            ttk.Label(f, text=t).grid(row=i, column=0, sticky="w")
            ttk.Entry(f, textvariable=v, width=w).grid(
                row=i, column=1, sticky="w")
            if i == 0:
                ttk.Button(f, text="Browse", command=lambda: self._pick(
                    self.comp_folder)).grid(row=i, column=2)

        def run():
            folder = self.comp_folder.get().strip()
            prefix = self.comp_prefix.get().strip()
            try:
                size = int(self.comp_canvas.get())
                grid = int(self.comp_grid.get())
            except ValueError:
                messagebox.showerror(
                    "Invalid input", "Canvas size and Grid size must be integers.")
                return
            if size <= 0 or grid <= 0:
                messagebox.showerror(
                    "Invalid input", "Canvas size and Grid size must be greater than zero.")
                return
            if size // grid <= 0:
                messagebox.showerror(
                    "Invalid input", "Canvas size must be >= Grid size.")
                return
            if not folder or not os.path.isdir(folder):
                messagebox.showerror(
                    "No folder", "Please choose a valid folder.")
                return
            if not prefix:
                messagebox.showerror(
                    "No prefix", "Please enter the filename prefix.")
                return

            def extract_index(name):
                try:
                    tail = name.split(prefix, 1)[1]
                    num_str = tail.split(".", 1)[0]
                    return int(num_str)
                except Exception:
                    return 0

            def work():
                jobs = []
                for root, _, fs in os.walk(folder):
                    imgs = [f for f in fs if f.endswith(
                        ".png") and prefix in f]
                    if imgs:
                        jobs.append((root, imgs, fs))

                def process(job):
                    root, imgs, fs = job
                    imgs = sorted(imgs, key=extract_index)
                    self._clear_comp_sprite_meta(root, fs)
                    cell = size // grid
                    slots = grid * grid
                    img = Image.new("RGBA", (size, size))
                    x = y = i = 0
                    c = 1
                    for n in imgs:
                        path = os.path.join(root, n)
                        with Image.open(path) as src:
                            resized = src.resize((cell, cell))
                        if resized.mode in ("RGBA", "LA"):
                            img.paste(
                                resized, (x * cell, (grid - 1 - y) * cell), resized)
                        elif "transparency" in resized.info:
                            rgba = resized.convert("RGBA")
                            img.paste(
                                rgba, (x * cell, (grid - 1 - y) * cell), rgba)
                            rgba.close()
                        else:
                            img.paste(
                                resized, (x * cell, (grid - 1 - y) * cell))
                        resized.close()
                        x += 1
                        i += 1
                        if x == grid:
                            x = 0
                            y += 1
                        if i == slots or n == imgs[-1]:
                            img.save(os.path.join(root, f"{c}.png"))
                            img.close()
                            img = None
                            c += 1
                            if n != imgs[-1]:
                                img = Image.new("RGBA", (size, size))
                                x = y = i = 0
                    if img is not None:
                        img.close()
                    for n in imgs:
                        os.remove(os.path.join(root, n))

                self._parallel_for_each(jobs, process, self._img_workers)

            self._run(lambda: self.comp_status.set(
                "Running"), work, lambda _: self.comp_status.set("DONE"))
        ttk.Button(f, text="Run", command=run).grid(
            row=4, column=0, columnspan=3)
        ttk.Label(f, textvariable=self.comp_status).grid(
            row=5, column=0, columnspan=3, sticky="w")

    def _clear_sprite_meta_slices(self, meta_path):
        try:
            with open(meta_path, "r", encoding="utf-8", errors="replace", newline="") as f:
                lines = f.readlines()
        except Exception:
            return False

        def skip_block(start_index, base_indent, include_dash):
            i = start_index
            while i < len(lines):
                base, _ = self._split_line_ending(lines[i])
                stripped = base.strip()
                if stripped == "":
                    i += 1
                    continue
                indent = len(base) - len(base.lstrip())
                if indent > base_indent:
                    i += 1
                    continue
                if include_dash and indent == base_indent and stripped.startswith("-"):
                    i += 1
                    continue
                break
            return i

        updated = False
        out = []
        i = 0
        while i < len(lines):
            line = lines[i]
            base, ending = self._split_line_ending(line)
            stripped = base.strip()
            indent = len(base) - len(base.lstrip())

            if stripped.startswith("internalIDToNameTable:"):
                out.append(f"{base[:indent]}internalIDToNameTable: []{ending}")
                updated = True
                i = skip_block(i + 1, indent, True)
                continue

            if stripped.startswith("sprites:"):
                out.append(f"{base[:indent]}sprites: []{ending}")
                updated = True
                i = skip_block(i + 1, indent, True)
                continue

            if stripped.startswith("nameFileIdTable:"):
                out.append(f"{base[:indent]}nameFileIdTable: {{}}{ending}")
                updated = True
                i = skip_block(i + 1, indent, False)
                continue

            out.append(line)
            i += 1

        if updated:
            with open(meta_path, "w", encoding="utf-8", newline="") as f:
                f.writelines(out)

        return updated

    def _clear_comp_sprite_meta(self, root, files):
        for name in files:
            lower = name.lower()
            if not lower.endswith(".png.meta"):
                continue
            base = name[: -len(".png.meta")]
            if base.isdigit():
                self._clear_sprite_meta_slices(os.path.join(root, name))

    def _desat(self):
        f = self._tab("Desaturate PNGs")
        self.desat_folder = tk.StringVar(self.r)
        self.desat_status = tk.StringVar(self.r, "Idle")
        ttk.Entry(f, textvariable=self.desat_folder, width=50).grid(
            row=0, column=1, sticky="we")
        ttk.Button(f, text="Browse", command=lambda: self._pick(
            self.desat_folder)).grid(row=0, column=2)

        def run():
            folder = self.desat_folder.get().strip()
            if not folder or not os.path.isdir(folder):
                messagebox.showerror(
                    "No folder", "Please choose a valid folder.")
                return

            def work():
                paths = list(walk(folder, [".png"]))

                def process(p):
                    with Image.open(p) as img:
                        rgba = img.convert("RGBA")
                    _, _, _, a = rgba.split()
                    g = rgba.convert("L")
                    merged = Image.merge("RGBA", (g, g, g, a))
                    merged.save(p)
                    merged.close()
                    rgba.close()
                    g.close()
                    a.close()

                self._parallel_for_each(paths, process, self._img_workers)

            self._run(lambda: self.desat_status.set(
                "Running"), work, lambda _: self.desat_status.set("DONE"))
        ttk.Button(f, text="Run", command=run).grid(
            row=1, column=0, columnspan=3)
        ttk.Label(f, textvariable=self.desat_status).grid(
            row=2, column=0, columnspan=3)

    def _dup(self):
        f = self._tab("Dup+Merge")
        self.dup_folder = tk.StringVar(self.r)
        self.dup_passes = tk.StringVar(self.r, "1")
        self.dup_inplace = tk.BooleanVar(self.r, True)
        self.dup_suffix = tk.StringVar(self.r, "_merged")
        self.dup_status = tk.StringVar(self.r, "Idle")
        ttk.Entry(f, textvariable=self.dup_folder, width=50).grid(
            row=0, column=1, sticky="we")
        ttk.Button(f, text="Browse", command=lambda: self._pick(
            self.dup_folder)).grid(row=0, column=2)
        ttk.Label(f, text="Passes:").grid(row=1, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.dup_passes, width=10).grid(
            row=1, column=1, sticky="w")
        ttk.Checkbutton(f, text="Overwrite files (in-place)",
                        variable=self.dup_inplace).grid(row=2, column=0, sticky="w")
        ttk.Label(f, text="Output suffix (only if not in-place):").grid(
            row=3, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.dup_suffix, width=15).grid(
            row=3, column=1, sticky="w")

        def run():
            folder = self.dup_folder.get().strip()
            if not folder or not os.path.isdir(folder):
                messagebox.showerror(
                    "No folder", "Please choose a valid folder.")
                return
            try:
                passes = int(self.dup_passes.get())
            except ValueError:
                messagebox.showerror(
                    "Invalid input", "Passes must be an integer.")
                return
            if passes < 0:
                messagebox.showerror(
                    "Invalid input", "Passes must be zero or greater.")
                return
            inplace = self.dup_inplace.get()
            suffix = self.dup_suffix.get()

            def work():
                paths = list(walk(folder, [".png"]))

                def process(p):
                    with Image.open(p) as img:
                        i = img.convert("RGBA")
                    o = i
                    for _ in range(passes):
                        next_o = Image.alpha_composite(o, o)
                        if o is not i:
                            o.close()
                        o = next_o
                    o.save(out(p, inplace, suffix))
                    i.close()
                    if o is not i:
                        o.close()

                self._parallel_for_each(paths, process, self._img_workers)

            self._run(lambda: self.dup_status.set(
                "Running"), work, lambda _: self.dup_status.set("DONE"))
        ttk.Button(f, text="Run", command=run).grid(
            row=4, column=0, columnspan=3)
        ttk.Label(f, textvariable=self.dup_status).grid(
            row=5, column=0, columnspan=3)

    def _crunch(self):
        f = self._tab("4x4 Crunch")
        self.crunch_folder = tk.StringVar(self.r)
        self.crunch_status = tk.StringVar(self.r, "Idle")
        ttk.Entry(f, textvariable=self.crunch_folder, width=50).grid(
            row=0, column=1, sticky="we")
        ttk.Button(f, text="Browse", command=lambda: self._pick(
            self.crunch_folder)).grid(row=0, column=2)

        def run():
            folder = self.crunch_folder.get().strip()
            if not folder or not os.path.isdir(folder):
                messagebox.showerror(
                    "No folder", "Please choose a valid folder.")
                return

            def work():
                paths = list(walk(folder, EXT))

                def process(p):
                    with Image.open(p) as img:
                        w, h = img.size
                        if w > A or h > A:
                            return
                        nw = ((w + 2 + B - 1) // B) * B
                        nh = ((h + 2 + B - 1) // B) * B
                        if nw == w and nh == h:
                            return
                        if nw > A or nh > A:
                            return
                        o = Image.new("RGBA", (nw, nh))
                        if img.mode in ("RGBA", "LA") or "transparency" in img.info:
                            rgba = img.convert("RGBA")
                            o.paste(rgba, (1, 1), rgba)
                            rgba.close()
                        else:
                            o.paste(img, (1, 1))
                        o.save(p)
                        o.close()

                self._parallel_for_each(paths, process, self._img_workers)

            self._run(lambda: self.crunch_status.set(
                "Running"), work, lambda _: self.crunch_status.set("DONE"))
        ttk.Button(f, text="Run", command=run).grid(
            row=1, column=0, columnspan=3)
        ttk.Label(f, textvariable=self.crunch_status).grid(
            row=2, column=0, columnspan=3)

    def _atlas(self):
        f = self._tab("Atlas 2048")
        self.atlas_folder = tk.StringVar(self.r)
        self.atlas_status = tk.StringVar(self.r, "Idle")
        ttk.Entry(f, textvariable=self.atlas_folder, width=50).grid(
            row=0, column=1, sticky="we")
        ttk.Button(f, text="Browse", command=lambda: self._pick(
            self.atlas_folder)).grid(row=0, column=2)

        def run():
            folder = self.atlas_folder.get().strip()
            if not folder or not os.path.isdir(folder):
                messagebox.showerror(
                    "No folder", "Please choose a valid folder.")
                return

            def work():
                jobs = []
                atlas_name = ATLAS.lower()
                for root, _, fs in os.walk(folder):
                    imgs = [
                        f for f in fs
                        if f.lower().endswith(".png") and f.lower() != atlas_name
                    ]
                    if imgs:
                        jobs.append((root, imgs))

                def process(job):
                    root, imgs = job
                    atlas = Image.new("RGBA", (A, A))
                    x = y = 0
                    rowh = 0
                    overflow = False
                    for n in imgs:
                        path = os.path.join(root, n)
                        with Image.open(path) as im:
                            if x + im.width > A:
                                x = 0
                                y += rowh
                                rowh = 0
                            if y + im.height > A:
                                overflow = True
                                break
                            if im.mode in ("RGBA", "LA") or "transparency" in im.info:
                                rgba = im.convert("RGBA")
                                atlas.paste(rgba, (x, y), rgba)
                                rgba.close()
                            else:
                                atlas.paste(im, (x, y))
                            x += im.width
                            rowh = max(rowh, im.height)
                        if overflow:
                            break
                    atlas.save(os.path.join(root, ATLAS))
                    atlas.close()
                    if not overflow:
                        for n in imgs:
                            os.remove(os.path.join(root, n))
                    return overflow

                results = self._parallel_map(jobs, process, self._img_workers)
                overflow_count = sum(1 for hit in results if hit)
                return overflow_count, len(results)

            def end(result):
                overflow_count, total = result
                if overflow_count:
                    self.atlas_status.set(
                        f"DONE - {overflow_count}/{total} folder(s) overflowed; originals kept.")
                else:
                    self.atlas_status.set("DONE")

            self._run(lambda: self.atlas_status.set(
                "Running"), work, end)
        ttk.Button(f, text="Build Atlases", command=run).grid(
            row=1, column=0, columnspan=3)
        ttk.Label(f, textvariable=self.atlas_status).grid(
            row=2, column=0, columnspan=3)

    def _slices(self):
        f = self._tab("Copy Sprite Slices")
        self.slices_root = tk.StringVar(self.r)
        self.slices_status = tk.StringVar(self.r, "Idle")
        ttk.Entry(f, textvariable=self.slices_root, width=50).grid(
            row=0, column=1, sticky="we")
        ttk.Button(f, text="Browse", command=lambda: self._pick(
            self.slices_root)).grid(row=0, column=2)

        def run():
            root_folder = self.slices_root.get().strip()
            if not root_folder or not os.path.isdir(root_folder):
                messagebox.showerror(
                    "Missing folder", "Please select a valid root folder.")
                return

            pairs, skipped_no_match, missing_meta = self._collect_slices_pairs(
                root_folder)
            if not pairs:
                msg = "No JPG targets matched PNG names."
                if missing_meta > 0 and skipped_no_match > 0:
                    msg = (
                        f"No copies made. Missing .meta for {missing_meta} PNG file(s). "
                        f"Skipped {skipped_no_match} PNG file(s) without matches."
                    )
                elif missing_meta > 0:
                    msg = (
                        f"No copies made. Missing .meta for {missing_meta} PNG file(s)."
                    )
                elif skipped_no_match > 0:
                    msg = (
                        f"No copies made. Skipped {skipped_no_match} PNG file(s) without matches."
                    )
                self.slices_status.set(msg)
                return

            existing_targets = sum(
                1 for _, target_meta in pairs if os.path.isfile(target_meta))
            if existing_targets:
                confirm = messagebox.askyesno(
                    "Overwrite target meta?",
                    f"This will replace {existing_targets} existing .meta file(s).\n\nContinue?"
                )
                if not confirm:
                    self.slices_status.set("Cancelled.")
                    return

            def work():
                def process(pair):
                    source_meta, target_meta = pair
                    try:
                        shutil.copyfile(source_meta, target_meta)
                        return True
                    except Exception:
                        return False

                results = self._parallel_map(
                    pairs, process, self._io_workers)
                copied = sum(1 for ok in results if ok)
                failed = len(results) - copied
                return copied, failed, skipped_no_match, missing_meta

            def end(result):
                copied, failed, skipped_no_match, missing_meta = result
                msg = f"DONE - copied slices to {copied} JPG file(s)."
                if failed:
                    msg = (
                        f"DONE - copied slices to {copied} JPG file(s), "
                        f"{failed} failed."
                    )
                if skipped_no_match:
                    msg += f" Skipped {skipped_no_match} PNG file(s) without matches."
                if missing_meta:
                    msg += f" {missing_meta} PNG file(s) missing .meta."
                self.slices_status.set(msg)
                if failed:
                    messagebox.showwarning(
                        "Copy issues",
                        f"Failed to copy {failed} .meta file(s)."
                    )

            self._run(lambda: self.slices_status.set(
                "Running"), work, end)
        ttk.Button(f, text="Copy", command=run).grid(
            row=1, column=0, columnspan=3)
        ttk.Label(f, textvariable=self.slices_status).grid(
            row=2, column=0, columnspan=3)

    def _collect_slices_pairs(self, root_folder):
        pairs = []
        skipped_no_match = 0
        missing_meta_sources = set()
        target_exts = {".jpg", ".jpeg"}

        for root, _, files in os.walk(root_folder):
            file_set = {name.lower() for name in files}
            pngs = []
            jpgs = []
            for file in files:
                ext = os.path.splitext(file)[1].lower()
                if ext == ".png":
                    pngs.append(file)
                elif ext in target_exts:
                    jpgs.append(file)

            if not pngs:
                continue

            if not jpgs:
                skipped_no_match += len(pngs)
                continue

            png_by_base = {}
            meta_by_base = {}
            missing_meta_by_base = {}
            for png in pngs:
                base = os.path.splitext(png)[0]
                base_key = base.lower()
                if base_key in png_by_base:
                    continue
                png_by_base[base_key] = png
                source_meta_name = f"{png}.meta"
                source_meta = os.path.join(root, source_meta_name)
                if source_meta_name.lower() in file_set:
                    meta_by_base[base_key] = source_meta
                else:
                    missing_meta_by_base[base_key] = source_meta

            png_bases = sorted(png_by_base.keys(), key=len, reverse=True)
            matched_bases = set()

            for jpg in jpgs:
                stem = os.path.splitext(jpg)[0]
                stem_key = stem.lower()
                best_base = None
                for base_key in png_bases:
                    if stem_key == base_key:
                        best_base = base_key
                        break
                    if (stem_key.startswith(base_key)
                            and stem_key[len(base_key):].isdigit()):
                        best_base = base_key
                        break
                if not best_base:
                    continue

                matched_bases.add(best_base)
                source_meta = meta_by_base.get(best_base)
                if not source_meta:
                    missing_meta_sources.add(missing_meta_by_base[best_base])
                    continue

                target_meta = os.path.join(root, jpg) + ".meta"
                pairs.append((source_meta, target_meta))

            skipped_no_match += max(0, len(png_by_base) - len(matched_bases))

        return pairs, skipped_no_match, len(missing_meta_sources)

    def _rename_files(self):
        f = self._tab("Rename Files")
        self.rename_folder = tk.StringVar(self.r)
        self.rename_match = tk.StringVar(self.r)
        self.rename_replace = tk.StringVar(self.r)
        self.rename_status = tk.StringVar(self.r, "Idle")

        row = 0
        ttk.Label(f, text="Folder:").grid(row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.rename_folder, width=50).grid(
            row=row, column=1, sticky="we")
        ttk.Button(f, text="Browse", command=lambda: self._pick(
            self.rename_folder)).grid(row=row, column=2)
        row += 1

        ttk.Label(f, text="Exact name to match (no extension):").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.rename_match, width=30).grid(
            row=row, column=1, sticky="w")
        row += 1

        ttk.Label(f, text="Replacement name (no extension):").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.rename_replace, width=30).grid(
            row=row, column=1, sticky="w")
        row += 1

        ttk.Label(
            f,
            text="Matches file name without extension; keeps the extension."
        ).grid(row=row, column=0, columnspan=3, sticky="w")
        row += 1

        def run():
            folder = self.rename_folder.get().strip()
            match_name = self.rename_match.get().strip()
            replace_name = self.rename_replace.get().strip()

            if not folder or not os.path.isdir(folder):
                messagebox.showerror(
                    "No folder", "Please choose a valid folder.")
                return
            if not match_name:
                messagebox.showerror(
                    "Missing name", "Please enter the exact name to match.")
                return
            if not replace_name:
                messagebox.showerror(
                    "Missing name", "Please enter the replacement name.")
                return
            if match_name == replace_name:
                messagebox.showerror(
                    "No change", "Match name and replacement name are the same.")
                return
            for name in (match_name, replace_name):
                if os.path.sep in name or (os.path.altsep and os.path.altsep in name):
                    messagebox.showerror(
                        "Invalid name", "Names must not include path separators.")
                    return

            def work():
                items = []
                for root, _, files in os.walk(folder):
                    for file_name in files:
                        base, ext = os.path.splitext(file_name)
                        if base != match_name:
                            continue
                        src = os.path.join(root, file_name)
                        dst = os.path.join(root, replace_name + ext)
                        items.append((src, dst))

                def process(item):
                    src, dst = item
                    if os.path.exists(dst) and os.path.normcase(src) != os.path.normcase(dst):
                        return "conflict"
                    try:
                        os.rename(src, dst)
                        return "renamed"
                    except Exception:
                        return "failed"

                results = self._parallel_map(
                    items, process, self._io_workers)
                renamed = sum(1 for r in results if r == "renamed")
                conflicts = sum(1 for r in results if r == "conflict")
                failed = sum(1 for r in results if r == "failed")
                return renamed, conflicts, failed

            def end(result):
                renamed, conflicts, failed = result
                msg = f"DONE - renamed {renamed} file(s)."
                if conflicts:
                    msg += f" Skipped {conflicts} existing target name(s)."
                if failed:
                    msg += f" {failed} failed."
                self.rename_status.set(msg)

            self._run(lambda: self.rename_status.set(
                "Running"), work, end)

        ttk.Button(f, text="Run", command=run).grid(
            row=row, column=0, columnspan=3)
        row += 1

        ttk.Label(f, textvariable=self.rename_status).grid(
            row=row, column=0, columnspan=3, sticky="w")

    def _spritelib(self):
        f = self._tab("Sprite Library Replace")
        self.sprite_lib_source_path = tk.StringVar(self.r)
        self.sprite_lib_target_path = tk.StringVar(self.r)
        self.sprite_lib_category = tk.StringVar(self.r)
        self.sprite_lib_atlas = tk.StringVar(self.r)
        self.sprite_lib_auto_find = tk.BooleanVar(self.r, False)
        self.sprite_lib_auto_jpg = tk.BooleanVar(self.r, True)
        self.sprite_lib_auto_folder = tk.StringVar(self.r)
        self.sprite_lib_status = tk.StringVar(self.r, "Idle")

        row = 0
        ttk.Label(f, text="Source Sprite Library asset (.spriteLib):").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_lib_source_path, width=50).grid(
            row=row, column=1, sticky="we")
        ttk.Button(f, text="Browse",
                   command=self._pick_sprite_library_source).grid(row=row, column=2)
        row += 1

        ttk.Label(f, text="Target Sprite Library asset (.spriteLib):").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_lib_target_path, width=50).grid(
            row=row, column=1, sticky="we")
        ttk.Button(f, text="Browse",
                   command=self._pick_sprite_library_target).grid(row=row, column=2)
        row += 1

        ttk.Label(f, text="Category to replace (manual):").grid(
            row=row, column=0, sticky="w")
        self.sprite_lib_category_combo = ttk.Combobox(
            f, textvariable=self.sprite_lib_category, width=28)
        self.sprite_lib_category_combo.grid(
            row=row, column=1, sticky="w")
        self.sprite_lib_category_button = ttk.Button(
            f, text="Load Categories", command=self._load_sprite_library_categories)
        self.sprite_lib_category_button.grid(row=row, column=2)
        row += 1

        ttk.Label(f, text="Atlas image (manual PNG/JPG):").grid(
            row=row, column=0, sticky="w")
        self.sprite_lib_atlas_entry = ttk.Entry(
            f, textvariable=self.sprite_lib_atlas, width=50)
        self.sprite_lib_atlas_entry.grid(row=row, column=1, sticky="we")
        self.sprite_lib_atlas_button = ttk.Button(
            f, text="Browse", command=self._pick_sprite_library_atlas)
        self.sprite_lib_atlas_button.grid(row=row, column=2)
        row += 1

        self.sprite_lib_auto_jpg_check = ttk.Checkbutton(
            f,
            text="Use JPG atlases (unchecked = PNG)",
            variable=self.sprite_lib_auto_jpg
        )
        self.sprite_lib_auto_jpg_check.grid(
            row=row, column=0, columnspan=3, sticky="w")
        row += 1

        self.sprite_lib_auto_find_check = ttk.Checkbutton(
            f,
            text="Auto-find atlas per category (folder)",
            variable=self.sprite_lib_auto_find,
            command=self._toggle_sprite_lib_auto_find
        )
        self.sprite_lib_auto_find_check.grid(
            row=row, column=0, columnspan=3, sticky="w")
        row += 1

        ttk.Label(f, text="Atlas folder (auto):").grid(
            row=row, column=0, sticky="w")
        self.sprite_lib_auto_folder_entry = ttk.Entry(
            f, textvariable=self.sprite_lib_auto_folder, width=50)
        self.sprite_lib_auto_folder_entry.grid(row=row, column=1, sticky="we")
        self.sprite_lib_auto_folder_button = ttk.Button(
            f, text="Browse", command=lambda: self._pick(self.sprite_lib_auto_folder)
        )
        self.sprite_lib_auto_folder_button.grid(row=row, column=2)
        row += 1

        ttk.Label(
            f,
            text="Manual mode uses a single atlas .meta for one category.\n"
                 "Auto-find scans a folder and processes all categories."
        ).grid(row=row, column=0, columnspan=3, sticky="w")
        row += 1

        ttk.Button(
            f, text="Replace Category Sprites", command=self._start_replace_sprite_library
        ).grid(row=row, column=0, columnspan=3)
        row += 1

        ttk.Label(f, textvariable=self.sprite_lib_status).grid(
            row=row, column=0, columnspan=3, sticky="w")
        self._toggle_sprite_lib_auto_find()

    def _toggle_sprite_lib_auto_find(self):
        auto = self.sprite_lib_auto_find.get()
        manual_state = "disabled" if auto else "normal"
        auto_state = "normal" if auto else "disabled"

        for name in ("sprite_lib_category_combo",
                     "sprite_lib_category_button",
                     "sprite_lib_atlas_entry",
                     "sprite_lib_atlas_button"):
            widget = getattr(self, name, None)
            if widget:
                widget.configure(state=manual_state)

        for name in ("sprite_lib_auto_folder_entry",
                     "sprite_lib_auto_folder_button"):
            widget = getattr(self, name, None)
            if widget:
                widget.configure(state=auto_state)

    def _spritelib_overwrite(self):
        f = self._tab("Sprite Library Overwrite")
        self.sprite_overwrite_path = tk.StringVar(self.r)
        self.sprite_overwrite_category = tk.StringVar(self.r)
        self.sprite_overwrite_root = tk.StringVar(self.r)
        self.sprite_overwrite_file = tk.StringVar(self.r, "1.png")
        self.sprite_overwrite_target = tk.StringVar(self.r, "fL")
        self.sprite_overwrite_status = tk.StringVar(self.r, "Idle")

        row = 0
        ttk.Label(f, text="Sprite Library asset (.spriteLib):").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_overwrite_path, width=50).grid(
            row=row, column=1, sticky="we")
        ttk.Button(
            f, text="Browse", command=self._pick_sprite_library_overwrite
        ).grid(row=row, column=2)
        row += 1

        ttk.Label(f, text="Target Category (optional):").grid(
            row=row, column=0, sticky="w")
        self.sprite_overwrite_category_combo = ttk.Combobox(
            f, textvariable=self.sprite_overwrite_category, width=28)
        self.sprite_overwrite_category_combo.grid(
            row=row, column=1, sticky="w")
        ttk.Button(
            f, text="Load Categories",
            command=self._load_sprite_library_categories_overwrite
        ).grid(row=row, column=2)
        row += 1

        ttk.Label(f, text="Scan Root Folder:").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_overwrite_root, width=50).grid(
            row=row, column=1, sticky="we")
        ttk.Button(f, text="Browse",
                   command=lambda: self._pick(self.sprite_overwrite_root)).grid(
            row=row, column=2)
        row += 1

        ttk.Label(f, text="Texture File Name:").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_overwrite_file, width=20).grid(
            row=row, column=1, sticky="w")
        row += 1

        ttk.Label(f, text="Target Folder Name:").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_overwrite_target, width=20).grid(
            row=row, column=1, sticky="w")
        row += 1

        ttk.Label(
            f,
            text="Example: Assets/Sprites/Characters/Ana/Run/Aqua/aa/fL/1.png\n"
                 "If category is blank, the root folder name is used.\n"
                 "File name can be '1.png' or an extension like 'png'."
        ).grid(row=row, column=0, columnspan=3, sticky="w")
        row += 1

        ttk.Button(
            f,
            text="Overwrite Library with New Labels",
            command=self._start_overwrite_sprite_library
        ).grid(row=row, column=0, columnspan=3)
        row += 1

        ttk.Label(f, textvariable=self.sprite_overwrite_status).grid(
            row=row, column=0, columnspan=3, sticky="w")

    def _spritelib_renumber(self):
        f = self._tab("Sprite Library Renumber")
        self.sprite_renumber_path = tk.StringVar(self.r)
        self.sprite_renumber_category = tk.StringVar(self.r)
        self.sprite_renumber_prefix = tk.StringVar(self.r)
        self.sprite_renumber_suffix = tk.StringVar(self.r)
        self.sprite_renumber_status = tk.StringVar(self.r, "Idle")

        row = 0
        ttk.Label(f, text="Sprite Library asset (.spriteLib):").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_renumber_path, width=50).grid(
            row=row, column=1, sticky="we")
        ttk.Button(f, text="Browse",
                   command=self._pick_sprite_library_renumber).grid(row=row, column=2)
        row += 1

        ttk.Label(f, text="Category to renumber:").grid(
            row=row, column=0, sticky="w")
        self.sprite_renumber_category_combo = ttk.Combobox(
            f, textvariable=self.sprite_renumber_category, width=28)
        self.sprite_renumber_category_combo.grid(
            row=row, column=1, sticky="w")
        ttk.Button(f, text="Load Categories",
                   command=self._load_sprite_library_categories_renumber).grid(row=row, column=2)
        row += 1

        ttk.Label(f, text="Prefix (optional):").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_renumber_prefix, width=20).grid(
            row=row, column=1, sticky="w")
        row += 1

        ttk.Label(f, text="Suffix (optional):").grid(
            row=row, column=0, sticky="w")
        ttk.Entry(f, textvariable=self.sprite_renumber_suffix, width=20).grid(
            row=row, column=1, sticky="w")
        row += 1

        ttk.Label(
            f,
            text="Renames labels in the chosen category to prefix + number + suffix\n"
                 "with numbering starting at 1."
        ).grid(row=row, column=0, columnspan=3, sticky="w")
        row += 1

        ttk.Button(
            f, text="Renumber Labels", command=self._start_renumber_sprite_library
        ).grid(row=row, column=0, columnspan=3)
        row += 1

        ttk.Label(f, textvariable=self.sprite_renumber_status).grid(
            row=row, column=0, columnspan=3, sticky="w")

    def _pick_sprite_library_source(self):
        file_path = filedialog.askopenfilename(
            title="Select Sprite Library asset",
            filetypes=[("Sprite Library", "*.spriteLib"), ("All files", "*.*")]
        )
        if file_path:
            self.sprite_lib_source_path.set(file_path)
            self._load_sprite_library_categories()

    def _pick_sprite_library_target(self):
        file_path = filedialog.askopenfilename(
            title="Select Sprite Library asset",
            filetypes=[("Sprite Library", "*.spriteLib"), ("All files", "*.*")]
        )
        if file_path:
            self.sprite_lib_target_path.set(file_path)

    def _pick_sprite_library_overwrite(self):
        file_path = filedialog.askopenfilename(
            title="Select Sprite Library asset",
            filetypes=[("Sprite Library", "*.spriteLib"), ("All files", "*.*")]
        )
        if file_path:
            self.sprite_overwrite_path.set(file_path)
            self._load_sprite_library_categories_overwrite()

    def _pick_sprite_library_renumber(self):
        file_path = filedialog.askopenfilename(
            title="Select Sprite Library asset",
            filetypes=[("Sprite Library", "*.spriteLib"), ("All files", "*.*")]
        )
        if file_path:
            self.sprite_renumber_path.set(file_path)
            self._load_sprite_library_categories_renumber()

    def _pick_sprite_library_atlas(self):
        use_jpg = self.sprite_lib_auto_jpg.get()
        if use_jpg:
            filetypes = [("Images", "*.jpg;*.jpeg;*.meta"),
                         ("All files", "*.*")]
        else:
            filetypes = [("Images", "*.png;*.meta"),
                         ("All files", "*.*")]
        file_path = filedialog.askopenfilename(
            title="Select atlas texture",
            filetypes=filetypes
        )
        if file_path:
            self.sprite_lib_atlas.set(file_path)

    def _load_sprite_library_categories(self):
        sprite_lib_path = self.sprite_lib_source_path.get().strip()
        if not sprite_lib_path or not os.path.isfile(sprite_lib_path):
            messagebox.showerror("Missing Sprite Library",
                                 "Please select a valid .spriteLib file.")
            return

        try:
            categories = self._extract_sprite_library_categories(
                sprite_lib_path)
        except Exception as e:
            messagebox.showerror("Error", str(e))
            return

        self.sprite_lib_category_combo["values"] = categories
        if categories and self.sprite_lib_category.get().strip() not in categories:
            self.sprite_lib_category.set(categories[0])
        if not categories:
            self.sprite_lib_status.set(
                "No categories found in the Sprite Library.")

    def _load_sprite_library_categories_overwrite(self):
        sprite_lib_path = self.sprite_overwrite_path.get().strip()
        if not sprite_lib_path or not os.path.isfile(sprite_lib_path):
            messagebox.showerror("Missing Sprite Library",
                                 "Please select a valid .spriteLib file.")
            return

        try:
            categories = self._extract_sprite_library_categories(
                sprite_lib_path)
        except Exception as e:
            messagebox.showerror("Error", str(e))
            return

        self.sprite_overwrite_category_combo["values"] = categories
        if categories and self.sprite_overwrite_category.get().strip() not in categories:
            self.sprite_overwrite_category.set(categories[0])
        if not categories:
            self.sprite_overwrite_status.set(
                "No categories found in the Sprite Library.")

    def _load_sprite_library_categories_renumber(self):
        sprite_lib_path = self.sprite_renumber_path.get().strip()
        if not sprite_lib_path or not os.path.isfile(sprite_lib_path):
            messagebox.showerror("Missing Sprite Library",
                                 "Please select a valid .spriteLib file.")
            return

        try:
            categories = self._extract_sprite_library_categories(
                sprite_lib_path)
        except Exception as e:
            messagebox.showerror("Error", str(e))
            return

        self.sprite_renumber_category_combo["values"] = categories
        if categories and self.sprite_renumber_category.get().strip() not in categories:
            self.sprite_renumber_category.set(categories[0])
        if not categories:
            self.sprite_renumber_status.set(
                "No categories found in the Sprite Library.")

    def _start_replace_sprite_library(self):
        source_path = self.sprite_lib_source_path.get().strip()
        target_path = self.sprite_lib_target_path.get().strip()
        category_name = self.sprite_lib_category.get().strip()
        atlas_path = self.sprite_lib_atlas.get().strip()
        auto_find = self.sprite_lib_auto_find.get()
        auto_folder = self.sprite_lib_auto_folder.get().strip()
        use_jpg = self.sprite_lib_auto_jpg.get()

        if not source_path or not os.path.isfile(source_path):
            messagebox.showerror("Missing source Sprite Library",
                                 "Please select a valid source .spriteLib file.")
            return
        if not target_path or not os.path.isfile(target_path):
            messagebox.showerror("Missing target Sprite Library",
                                 "Please select a valid target .spriteLib file.")
            return
        if auto_find:
            if not auto_folder or not os.path.isdir(auto_folder):
                messagebox.showerror("Missing atlas folder",
                                     "Please select a valid atlas folder.")
                return
        else:
            if not category_name:
                messagebox.showerror("Missing category",
                                     "Please select or enter a category name.")
                return
            if not atlas_path or not os.path.isfile(atlas_path):
                messagebox.showerror("Missing atlas",
                                     "Please select a valid atlas image file.")
                return
            image_path = atlas_path
            if atlas_path.lower().endswith(".meta"):
                image_path = atlas_path[:-5]
            ext = os.path.splitext(image_path)[1].lower()
            if use_jpg:
                valid_exts = (".jpg", ".jpeg")
                expected = "JPG"
            else:
                valid_exts = (".png",)
                expected = "PNG"
            if ext not in valid_exts:
                messagebox.showerror(
                    "Atlas type mismatch",
                    f"Expected a {expected} atlas (toggle the JPG option if needed)."
                )
                return

        confirm_msg = (
            "This will modify the target Sprite Library asset in-place.\n\nContinue?"
        )
        if auto_find:
            confirm_msg = (
                "This will modify the target Sprite Library asset in-place\n"
                "for all categories using the selected folder.\n\nContinue?"
            )
        confirm = messagebox.askyesno("Confirm replacement", confirm_msg)
        if not confirm:
            return

        def work():
            if auto_find:
                assets_root = self._find_assets_root(source_path)
                guid_to_meta_path = self._build_guid_to_meta_index(assets_root)
                guid_to_fileid_name = {}
                guid_index_complete = True
                return self._replace_sprite_library_auto_folder(
                    source_path,
                    target_path,
                    auto_folder,
                    use_jpg,
                    guid_to_fileid_name,
                    guid_to_meta_path,
                    guid_index_complete
                )

            meta_path = atlas_path
            if not atlas_path.lower().endswith(".meta"):
                meta_path = atlas_path + ".meta"
            atlas_file = meta_path[:-
                                   5] if meta_path.lower().endswith(".meta") else meta_path

            if not os.path.isfile(meta_path):
                raise FileNotFoundError(f"Atlas .meta not found:\n{meta_path}")

            normalized_category = self._normalize_entry_name(category_name)
            source_categories = self._extract_sprite_library_categories(
                source_path)
            source_found = any(
                self._normalize_entry_name(category) == normalized_category
                for category in source_categories
            )
            if not source_found:
                return {
                    "mode": "single",
                    "updated": 0,
                    "missing_atlas": 0,
                    "missing_labels": 0,
                    "missing_source_sprites": 0,
                    "source_found": False,
                    "target_found": False,
                }

            atlas_series = self._build_atlas_series(atlas_file)
            if atlas_series:
                sprite_sequence = self._build_sprite_sequence_from_series(
                    atlas_series)
            else:
                atlas_guid, sprite_entries = self._load_sprite_sheet_entries(
                    meta_path)
                if not atlas_guid:
                    raise ValueError("Atlas .meta missing guid.")
                sprite_sequence = [
                    (file_id, atlas_guid) for _, file_id in sprite_entries
                ]

            if not sprite_sequence:
                raise ValueError("Atlas .meta has no sprite slice names.")

            updated, missing_atlas, missing_labels, target_found = (
                self._replace_sprite_library_category_sequential(
                    target_path,
                    category_name,
                    sprite_sequence
                )
            )
            return {
                "mode": "single",
                "updated": updated,
                "missing_atlas": missing_atlas,
                "missing_labels": missing_labels,
                "missing_source_sprites": 0,
                "source_found": True,
                "target_found": target_found,
            }

        def end(result):
            if result.get("mode") == "auto":
                categories_total = result.get("categories_total", 0)
                if categories_total == 0:
                    self.sprite_lib_status.set(
                        "No categories found in the source Sprite Library.")
                    return
                updated = result.get("updated", 0)
                missing_atlas = result.get("missing_atlas", 0)
                missing_labels = result.get("missing_labels", 0)
                missing_source_sprites = result.get(
                    "missing_source_sprites", 0)
                missing_atlas_categories = result.get(
                    "missing_atlas_categories", 0)
                missing_target_categories = result.get(
                    "missing_target_categories", 0)
                categories_processed = result.get("categories_processed", 0)

                msg = (
                    f"DONE - updated {updated} label(s) across "
                    f"{categories_processed}/{categories_total} category(s)."
                )
                if missing_atlas_categories:
                    msg += f" {missing_atlas_categories} category(s) missing atlas."
                if missing_target_categories:
                    msg += f" {missing_target_categories} category(s) missing in target."
                if missing_atlas:
                    msg += f" {missing_atlas} sprite name(s) not found in atlas."
                if missing_labels:
                    msg += f" {missing_labels} label(s) missing in target."
                if missing_source_sprites:
                    msg += f" {missing_source_sprites} label(s) missing sprite in source."
                self.sprite_lib_status.set(msg)
                if missing_atlas:
                    messagebox.showwarning(
                        "Missing sprite names",
                        f"{missing_atlas} sprite name(s) were not found in the atlas."
                    )
                return

            updated = result.get("updated", 0)
            missing_atlas = result.get("missing_atlas", 0)
            missing_labels = result.get("missing_labels", 0)
            missing_source_sprites = result.get("missing_source_sprites", 0)
            source_found = result.get("source_found", True)
            target_found = result.get("target_found", True)
            if not source_found:
                self.sprite_lib_status.set(
                    f"Source category not found: {category_name}")
                return
            if not target_found:
                self.sprite_lib_status.set(
                    f"Target category not found: {category_name}")
                return
            msg = f"DONE - updated {updated} label(s)."
            if missing_atlas:
                msg += f" {missing_atlas} sprite name(s) not found in atlas."
            if missing_labels:
                msg += f" {missing_labels} label(s) missing in target."
            if missing_source_sprites:
                msg += f" {missing_source_sprites} label(s) missing sprite in source."
            self.sprite_lib_status.set(msg)
            if missing_atlas:
                messagebox.showwarning(
                    "Missing sprite names",
                    f"{missing_atlas} sprite name(s) were not found in the atlas."
                )

        self._run(lambda: self.sprite_lib_status.set(
            "Running"), work, end)

    def _start_overwrite_sprite_library(self):
        sprite_lib_path = self.sprite_overwrite_path.get().strip()
        category_name = self.sprite_overwrite_category.get().strip()
        root_folder = self.sprite_overwrite_root.get().strip()
        file_name = self.sprite_overwrite_file.get().strip()
        target_folder = self.sprite_overwrite_target.get().strip()

        if not sprite_lib_path or not os.path.isfile(sprite_lib_path):
            messagebox.showerror("Missing Sprite Library",
                                 "Please select a valid .spriteLib file.")
            return
        if not root_folder or not os.path.isdir(root_folder):
            messagebox.showerror("Missing folder",
                                 "Please select a valid scan root folder.")
            return
        if not file_name:
            messagebox.showerror("Missing file name",
                                 "Please enter a texture file name.")
            return
        if not target_folder:
            messagebox.showerror("Missing folder name",
                                 "Please enter a target folder name.")
            return

        if not category_name:
            category_name = os.path.basename(os.path.normpath(root_folder))

        confirm = messagebox.askyesno(
            "Confirm overwrite",
            "This will replace labels in the selected Sprite Library category.\n\nContinue?"
        )
        if not confirm:
            return

        def work():
            return self._overwrite_sprite_library_from_folders(
                sprite_lib_path,
                category_name,
                root_folder,
                target_folder,
                file_name
            )

        def end(result):
            sprites_added = result.get("sprites_added", 0)
            missing_files = result.get("missing_files", 0)
            missing_meta = result.get("missing_meta", 0)
            missing_sprites = result.get("missing_sprites", 0)
            created_category = result.get("created_category", False)
            category = result.get("category", category_name)

            if sprites_added == 0:
                self.sprite_overwrite_status.set("No sprites found to add.")
                return

            msg = f"DONE - added {sprites_added} sprite(s) to '{category}'."
            if created_category:
                msg += " Created category."
            if missing_files:
                msg += f" {missing_files} missing file(s)."
            if missing_meta:
                msg += f" {missing_meta} file(s) missing .meta."
            if missing_sprites:
                msg += f" {missing_sprites} file(s) had no sprites."
            self.sprite_overwrite_status.set(msg)
            if missing_files or missing_meta or missing_sprites:
                messagebox.showwarning(
                    "Overwrite completed with warnings", msg)

        self._run(lambda: self.sprite_overwrite_status.set(
            "Running"), work, end)

    def _start_renumber_sprite_library(self):
        sprite_lib_path = self.sprite_renumber_path.get().strip()
        category_name = self.sprite_renumber_category.get().strip()
        prefix = self.sprite_renumber_prefix.get()
        suffix = self.sprite_renumber_suffix.get()

        if not sprite_lib_path or not os.path.isfile(sprite_lib_path):
            messagebox.showerror("Missing Sprite Library",
                                 "Please select a valid .spriteLib file.")
            return
        if not category_name:
            messagebox.showerror("Missing category",
                                 "Please select or enter a category name.")
            return

        confirm = messagebox.askyesno(
            "Confirm rename",
            "This will modify the Sprite Library asset in-place.\n\nContinue?"
        )
        if not confirm:
            return

        def work():
            return self._renumber_sprite_library_category(
                sprite_lib_path,
                category_name,
                prefix,
                suffix
            )

        def end(result):
            updated, category_found = result
            if not category_found:
                self.sprite_renumber_status.set(
                    f"Category not found: {category_name}")
                return
            self.sprite_renumber_status.set(
                f"DONE - renamed {updated} label(s).")

        self._run(lambda: self.sprite_renumber_status.set(
            "Running"), work, end)

    def _overwrite_sprite_library_from_folders(self, sprite_lib_path, category_name, root_folder, target_subfolder, file_name):
        folders = []
        for dirpath, _, _ in os.walk(root_folder):
            if dirpath == root_folder:
                continue
            if os.path.basename(dirpath) == target_subfolder:
                folders.append(dirpath)
        folders.sort()

        entries = []
        missing_files = 0
        missing_meta = 0
        missing_sprites = 0

        for folder in folders:
            base_default = self._build_label_base_from_folder(folder)
            groups, missing_expected = self._resolve_overwrite_groups(
                folder, file_name, base_default)
            if missing_expected:
                missing_files += missing_expected
            if not groups:
                continue

            for base_label, files_to_process in groups:
                label_counter = 1
                for file_path in files_to_process:
                    guid, sprite_entries = self._load_sprite_entries_from_meta(
                        file_path)
                    if not guid:
                        missing_meta += 1
                        continue
                    if not sprite_entries:
                        missing_sprites += 1
                        continue

                    for _, file_id in sprite_entries:
                        label = f"{base_label}_{label_counter}"
                        entries.append({
                            "label": label,
                            "file_id": file_id,
                            "guid": guid,
                        })
                        label_counter += 1

        if not entries:
            return {
                "sprites_added": 0,
                "missing_files": missing_files,
                "missing_meta": missing_meta,
                "missing_sprites": missing_sprites,
                "created_category": False,
                "category": category_name,
            }

        created_category = self._overwrite_sprite_library_category(
            sprite_lib_path, category_name, entries)

        return {
            "sprites_added": len(entries),
            "missing_files": missing_files,
            "missing_meta": missing_meta,
            "missing_sprites": missing_sprites,
            "created_category": created_category,
            "category": category_name,
        }

    def _build_label_base_from_folder(self, folder):
        parent = os.path.dirname(folder)
        grandparent = os.path.dirname(parent)
        if not parent or not grandparent:
            return None
        return f"{os.path.basename(grandparent)}_{os.path.basename(parent)}"

    def _resolve_overwrite_groups(self, folder, file_name, base_default):
        files, missing_expected = self._resolve_overwrite_files(
            folder, file_name)
        if files:
            if not base_default:
                return [], missing_expected
            return [(base_default, files)], missing_expected

        groups = []
        missing_total = 0
        subdirs = []
        for entry in os.scandir(folder):
            if entry.is_dir():
                subdirs.append(entry.path)
        subdirs.sort()

        for subdir in subdirs:
            sub_files, sub_missing = self._resolve_overwrite_files(
                subdir, file_name)
            if sub_files:
                groups.append((os.path.basename(subdir), sub_files))
            elif sub_missing:
                missing_total += 1

        if groups:
            return groups, missing_total
        return groups, missing_expected + missing_total

    def _resolve_overwrite_files(self, folder, file_name):
        is_extension_only = (
            "." not in file_name
            and file_name
            and not file_name[0].isdigit()
            and len(file_name) <= 4
        )
        if is_extension_only:
            files = []
            for entry in os.scandir(folder):
                if not entry.is_file():
                    continue
                if entry.name.lower().endswith(".meta"):
                    continue
                if file_name.lower() in entry.name.lower():
                    files.append(entry.path)
            files.sort(key=self._sort_file_by_numeric_name)
            return files, False

        base, ext = os.path.splitext(file_name)
        if ext and base.isdigit():
            files = []
            idx = int(base)
            while True:
                candidate = os.path.join(folder, f"{idx}{ext}")
                if not os.path.isfile(candidate):
                    break
                files.append(candidate)
                idx += 1
            if not files:
                return [], True
            return files, False

        candidate = os.path.join(folder, file_name)
        if not os.path.isfile(candidate):
            return [], True
        return [candidate], False

    def _sort_file_by_numeric_name(self, path):
        name = Path(path).stem
        if name.isdigit():
            return int(name)
        return 2 ** 31 - 1

    def _load_sprite_entries_from_meta(self, image_path):
        meta_path = image_path + ".meta"
        if not os.path.isfile(meta_path):
            return None, []

        guid, sprite_entries = self._load_sprite_sheet_entries(meta_path)
        if not guid:
            return None, []
        if not sprite_entries:
            return guid, []

        return guid, sprite_entries

    def _sort_sprite_names_by_frame_index(self, names):
        sorted_by_index = {}
        unsorted = []

        for name in names:
            parts = name.split("_")
            if len(parts) >= 2 and parts[1].isdigit():
                sorted_by_index[int(parts[1])] = name
            else:
                unsorted.append(name)

        ordered = unsorted
        ordered.extend(
            [sorted_by_index[idx] for idx in sorted(sorted_by_index)]
        )
        return ordered

    def _overwrite_sprite_library_category(self, sprite_lib_path, category_name, entries):
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        if not lines:
            raise ValueError("Sprite Library file is empty.")

        line_ending = self._detect_line_ending(lines)
        library_start, library_end, categories = (
            self._index_sprite_library_categories_exact(lines)
        )
        if library_start is None:
            raise ValueError("Sprite Library file is missing m_Library.")

        target_norm = self._normalize_entry_name(category_name)
        target_entry = None
        for entry in categories:
            if entry["norm"] == target_norm:
                target_entry = entry
                break

        block = self._build_sprite_library_category_block(
            category_name, entries, line_ending)
        if target_entry:
            lines = lines[:target_entry["start"]] + \
                block + lines[target_entry["end"]:]
            created_category = False
        else:
            insert_at = library_end if library_end is not None else len(lines)
            lines = lines[:insert_at] + block + lines[insert_at:]
            created_category = True

        with open(sprite_lib_path, "w", encoding="utf-8", newline="") as f:
            f.writelines(lines)

        return created_category

    def _build_sprite_library_category_block(self, category_name, entries, line_ending):
        label = self._format_sprite_label(category_name, category_name)
        category_hash = self._sprite_library_hash(category_name)
        lines = [
            f"  - m_Name: {label}{line_ending}",
            f"    m_Hash: {category_hash}{line_ending}",
            f"    m_CategoryList: []{line_ending}",
        ]

        if entries:
            lines.append(f"    m_OverrideEntries:{line_ending}")
            lines.extend(self._build_sprite_library_override_entries(
                entries, line_ending))
        else:
            lines.append(f"    m_OverrideEntries: []{line_ending}")

        lines.append(f"    m_FromMain: 0{line_ending}")
        lines.append(f"    m_EntryOverrideCount: {len(entries)}{line_ending}")
        return lines

    def _build_sprite_library_override_entries(self, entries, line_ending):
        lines = []
        for entry in entries:
            label = entry["label"]
            label_value = self._format_sprite_label(label, label)
            label_hash = self._sprite_library_hash(label)
            file_id = entry["file_id"]
            guid = entry["guid"]
            lines.append(f"    - m_Name: {label_value}{line_ending}")
            lines.append(f"      m_Hash: {label_hash}{line_ending}")
            lines.append(
                f"      m_Sprite: {{fileID: {file_id}, guid: {guid}, type: 3}}{line_ending}"
            )
            lines.append(f"      m_FromMain: 0{line_ending}")
            lines.append(
                f"      m_SpriteOverride: {{fileID: {file_id}, guid: {guid}, type: 3}}{line_ending}"
            )
        return lines

    def _sprite_library_hash(self, value):
        if not value:
            return 0
        return zlib.crc32(value.encode("utf-8")) & 0x3fffffff

    def _detect_line_ending(self, lines):
        for line in lines:
            if line.endswith("\r\n"):
                return "\r\n"
            if line.endswith("\n"):
                return "\n"
        return "\n"

    def _index_sprite_library_categories_exact(self, lines):
        categories = []
        library_start = None
        library_end = None
        in_library = False
        current = None

        for i, line in enumerate(lines):
            stripped = line.strip()
            if stripped == "m_Library:":
                in_library = True
                library_start = i
                continue
            if not in_library:
                continue

            indent = len(line) - len(line.lstrip())
            if indent == 2 and line.startswith("  - m_Name: "):
                if current:
                    current["end"] = i
                    categories.append(current)
                name = line.split(":", 1)[1].strip()
                current = {
                    "name": name,
                    "norm": self._normalize_entry_name(name),
                    "start": i,
                }
                continue

            if indent == 2 and stripped != "" and not line.startswith("  - "):
                library_end = i
                if current:
                    current["end"] = i
                    categories.append(current)
                    current = None
                in_library = False
                break

        if in_library and current:
            current["end"] = library_end if library_end is not None else len(
                lines)
            categories.append(current)

        if library_start is not None and library_end is None:
            library_end = len(lines)

        return library_start, library_end, categories

    def _extract_sprite_library_categories(self, sprite_lib_path):
        categories = []
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        in_library = False
        for line in lines:
            stripped = line.strip()
            if stripped == "m_Library:":
                in_library = True
                continue
            if in_library and line.startswith("  - m_Name: "):
                name = line.split(":", 1)[1].strip()
                if name and name not in categories:
                    categories.append(name)

        return categories

    def _index_sprite_library_categories(self, lines):
        categories = []
        in_library = False
        current = None

        for i, line in enumerate(lines):
            stripped = line.strip()
            if stripped == "m_Library:":
                in_library = True
                continue
            if in_library and line.startswith("  - m_Name: "):
                if current:
                    current["end"] = i
                    categories.append(current)
                name = line.split(":", 1)[1].strip()
                current = {
                    "name": name,
                    "norm": self._normalize_entry_name(name),
                    "start": i,
                }
                continue
            if in_library and stripped != "" and not line.startswith("  "):
                break

        if current:
            current["end"] = len(lines)
            categories.append(current)

        return categories

    def _extract_sprite_library_source_map(self, sprite_lib_path, guid_to_fileid_name=None, guid_to_meta_path=None, guid_index_complete=False):
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        if guid_to_fileid_name is None:
            guid_to_fileid_name = {}
        if guid_to_meta_path is None:
            guid_to_meta_path = {}

        categories = []
        category_data = {}
        missing_source = {}

        in_library = False
        in_entries = False
        current_category = None
        current_label = None
        current_label_has_override = False
        assets_root = self._find_assets_root(sprite_lib_path)

        for line in lines:
            stripped = line.strip()
            if stripped == "m_Library:":
                in_library = True
                in_entries = False
                current_category = None
                current_label = None
                current_label_has_override = False
                continue

            if in_library and line.startswith("  - m_Name: "):
                category_raw = line.split(":", 1)[1].strip()
                category_norm = self._normalize_entry_name(category_raw)
                categories.append(category_raw)
                entry = category_data.get(category_norm)
                if not entry:
                    entry = {
                        "name": category_raw,
                        "label_to_sprite": {},
                        "label_to_meta": {},
                    }
                    category_data[category_norm] = entry
                current_category = category_norm
                in_entries = False
                current_label = None
                current_label_has_override = False
                continue

            if not in_library or not current_category:
                continue

            if stripped == "m_OverrideEntries:":
                in_entries = True
                current_label = None
                current_label_has_override = False
                continue

            if in_entries and line.startswith("    - m_Name: "):
                current_label = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                entry = category_data[current_category]
                if current_label not in entry["label_to_sprite"]:
                    entry["label_to_sprite"][current_label] = None
                    entry["label_to_meta"][current_label] = None
                current_label_has_override = False
                continue

            if in_entries and current_label:
                if stripped.startswith("m_SpriteOverride:"):
                    file_id, guid = self._parse_sprite_ref_line(line)
                    override_ref = self._has_valid_sprite_ref(file_id, guid)
                    sprite_name, meta_path = self._sprite_name_and_meta_from_ref(
                        file_id, guid, assets_root, guid_to_fileid_name, guid_to_meta_path, guid_index_complete)
                    entry = category_data[current_category]
                    if meta_path:
                        entry["label_to_meta"][current_label] = meta_path
                    if sprite_name:
                        entry["label_to_sprite"][current_label] = sprite_name
                    if override_ref:
                        current_label_has_override = True
                    continue
                if stripped.startswith("m_Sprite:"):
                    if current_label_has_override:
                        continue
                    file_id, guid = self._parse_sprite_ref_line(line)
                    sprite_name, meta_path = self._sprite_name_and_meta_from_ref(
                        file_id, guid, assets_root, guid_to_fileid_name, guid_to_meta_path, guid_index_complete)
                    entry = category_data[current_category]
                    if meta_path:
                        entry["label_to_meta"][current_label] = meta_path
                    if sprite_name:
                        entry["label_to_sprite"][current_label] = sprite_name

        for category_norm, entry in category_data.items():
            missing_source[category_norm] = sum(
                1 for name in entry["label_to_sprite"].values() if not name)

        return categories, category_data, missing_source

    def _extract_sprite_library_label_sprite_names(self, sprite_lib_path, category_name, guid_to_fileid_name=None, guid_to_meta_path=None, guid_index_complete=False):
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        label_to_sprite = {}
        label_to_meta = {}
        category_found = False
        in_library = False
        in_category = False
        in_entries = False
        current_label = None
        current_label_has_override = False
        assets_root = self._find_assets_root(sprite_lib_path)
        if guid_to_fileid_name is None:
            guid_to_fileid_name = {}
        if guid_to_meta_path is None:
            guid_to_meta_path = {}
        normalized_target = self._normalize_entry_name(category_name)

        for line in lines:
            stripped = line.strip()
            if stripped == "m_Library:":
                in_library = True
                in_category = False
                in_entries = False
                current_label = None
                current_label_has_override = False
                continue

            if in_library and line.startswith("  - m_Name: "):
                current_category = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                in_category = (current_category == normalized_target)
                if in_category:
                    category_found = True
                in_entries = False
                current_label = None
                current_label_has_override = False
                continue

            if in_category and stripped == "m_OverrideEntries:":
                in_entries = True
                current_label = None
                current_label_has_override = False
                continue

            if in_category and in_entries and line.startswith("    - m_Name: "):
                current_label = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                if current_label not in label_to_sprite:
                    label_to_sprite[current_label] = None
                    label_to_meta[current_label] = None
                current_label_has_override = False
                continue

            if in_category and in_entries and current_label:
                if stripped.startswith("m_SpriteOverride:"):
                    file_id, guid = self._parse_sprite_ref_line(line)
                    override_ref = self._has_valid_sprite_ref(file_id, guid)
                    sprite_name, meta_path = self._sprite_name_and_meta_from_ref(
                        file_id, guid, assets_root, guid_to_fileid_name, guid_to_meta_path, guid_index_complete)
                    if meta_path:
                        label_to_meta[current_label] = meta_path
                    if sprite_name:
                        label_to_sprite[current_label] = sprite_name
                    if override_ref:
                        current_label_has_override = True
                    continue
                if stripped.startswith("m_Sprite:"):
                    if current_label_has_override:
                        continue
                    file_id, guid = self._parse_sprite_ref_line(line)
                    sprite_name, meta_path = self._sprite_name_and_meta_from_ref(
                        file_id, guid, assets_root, guid_to_fileid_name, guid_to_meta_path, guid_index_complete)
                    if meta_path:
                        label_to_meta[current_label] = meta_path
                    if sprite_name:
                        label_to_sprite[current_label] = sprite_name

        missing_sprite_count = sum(
            1 for name in label_to_sprite.values() if not name)
        return label_to_sprite, label_to_meta, category_found, missing_sprite_count

    def _has_valid_sprite_ref(self, file_id, guid):
        if not file_id or not guid:
            return False
        file_id = file_id.strip()
        guid = guid.strip()
        if file_id == "0":
            return False
        if guid == "00000000000000000000000000000000":
            return False
        return True

    def _sprite_name_and_meta_from_ref(self, file_id, guid, assets_root, guid_to_fileid_name, guid_to_meta_path, guid_index_complete=False):
        if not file_id or not guid:
            return None, None
        file_id = file_id.strip()
        guid = guid.strip()
        if file_id == "0":
            return None, None
        if guid == "00000000000000000000000000000000":
            return None, None

        meta_path = guid_to_meta_path.get(guid)
        if meta_path is None:
            if guid_index_complete:
                guid_to_meta_path[guid] = None
                guid_to_fileid_name[guid] = {}
                return None, None
            meta_path = self._find_meta_by_guid(assets_root, guid)
            guid_to_meta_path[guid] = meta_path
        if not meta_path:
            guid_to_fileid_name[guid] = {}
            return None, None

        fileid_to_name = guid_to_fileid_name.get(guid)
        if fileid_to_name is None:
            fileid_to_name = self._build_fileid_to_name(meta_path)
            guid_to_fileid_name[guid] = fileid_to_name

        return fileid_to_name.get(file_id), meta_path

    def _sprite_name_from_ref(self, file_id, guid, assets_root, guid_to_fileid_name):
        sprite_name, _ = self._sprite_name_and_meta_from_ref(
            file_id, guid, assets_root, guid_to_fileid_name, {})
        return sprite_name

    def _build_atlas_meta_index(self, root_folder, use_jpg):
        exts = (".jpg", ".jpeg") if use_jpg else (".png",)
        meta_cache = {}
        name_index = {}

        image_paths = list(iter_files(root_folder, exts))

        def process(image_path):
            meta_path = image_path + ".meta"
            if not os.path.isfile(meta_path):
                return None
            guid, name_to_id = self._parse_sprite_meta(meta_path)
            if not guid or not name_to_id:
                return None
            return image_path, guid, name_to_id

        results = self._parallel_map(image_paths, process, self._io_workers)
        for item in results:
            if not item:
                continue
            image_path, guid, name_to_id = item
            meta_cache[image_path] = {
                "guid": guid,
                "name_to_id": name_to_id,
            }
            for sprite_name in name_to_id:
                name_index.setdefault(sprite_name, set()).add(image_path)

        return meta_cache, name_index

    def _find_best_atlas_for_sprites(self, sprite_names, name_index):
        counts = {}
        for sprite_name in sprite_names:
            for path in name_index.get(sprite_name, ()):
                counts[path] = counts.get(path, 0) + 1
        if not counts:
            return None
        return max(counts.items(), key=lambda item: (item[1], item[0]))[0]

    def _build_atlas_series_cached(self, atlas_file, meta_cache):
        path = Path(atlas_file)
        stem = path.stem
        match = re.match(r"^(.*?)(\d+)$", stem)
        if not match:
            return None
        prefix, num_str = match.groups()
        num = int(num_str)
        width = len(num_str)
        ext = path.suffix
        folder = path.parent
        series = []

        while True:
            if width > 1:
                candidate_stem = f"{prefix}{num:0{width}d}"
            else:
                candidate_stem = f"{prefix}{num}"
            candidate = os.path.normpath(
                str(folder / f"{candidate_stem}{ext}")
            )
            entry = meta_cache.get(candidate)
            if not entry:
                break
            name_to_id = entry["name_to_id"]
            guid = entry["guid"]
            count = self._count_numeric_suffixes(name_to_id, candidate_stem)
            series.append({
                "num": num,
                "base": candidate_stem,
                "path": candidate,
                "meta": candidate + ".meta",
                "guid": guid,
                "name_to_id": name_to_id,
                "count": count,
            })
            num += 1

        return series or None

    def _normalize_atlas_series_start(self, atlas_path, meta_cache):
        path = Path(atlas_path)
        stem = path.stem
        match = re.match(r"^(.*?)(\d+)$", stem)
        if not match:
            return os.path.normpath(atlas_path)
        prefix, num_str = match.groups()
        num = int(num_str)
        width = len(num_str)
        ext = path.suffix
        folder = path.parent

        start_num = num
        while start_num > 0:
            prev_num = start_num - 1
            if width > 1:
                candidate_stem = f"{prefix}{prev_num:0{width}d}"
            else:
                candidate_stem = f"{prefix}{prev_num}"
            candidate = os.path.normpath(
                str(folder / f"{candidate_stem}{ext}")
            )
            if candidate not in meta_cache:
                break
            start_num = prev_num

        if start_num == num:
            return os.path.normpath(atlas_path)
        if width > 1:
            start_stem = f"{prefix}{start_num:0{width}d}"
        else:
            start_stem = f"{prefix}{start_num}"
        return os.path.normpath(str(folder / f"{start_stem}{ext}"))

    def _get_cached_atlas_data(self, atlas_path, meta_cache, series_cache):
        atlas_path = self._normalize_atlas_series_start(atlas_path, meta_cache)
        cached = series_cache.get(atlas_path)
        if cached:
            return cached

        atlas_series = self._build_atlas_series_cached(atlas_path, meta_cache)
        if atlas_series:
            atlas_guid = atlas_series[0]["guid"]
            name_to_id = atlas_series[0]["name_to_id"]
        else:
            entry = meta_cache.get(atlas_path)
            atlas_guid = entry["guid"] if entry else None
            name_to_id = entry["name_to_id"] if entry else None

        series_cache[atlas_path] = (atlas_guid, name_to_id, atlas_series)
        return atlas_guid, name_to_id, atlas_series

    def _path_is_under(self, path, root):
        if not path or not root:
            return False
        path_norm = os.path.normcase(os.path.normpath(path))
        root_norm = os.path.normcase(os.path.normpath(root))
        if path_norm == root_norm:
            return True
        return path_norm.startswith(root_norm + os.sep)

    def _label_paths_under_root(self, label_to_meta, root_folder):
        if not label_to_meta:
            return False
        any_found = False
        for meta_path in label_to_meta.values():
            if not meta_path:
                continue
            any_found = True
            image_path = meta_path[:-
                                   5] if meta_path.lower().endswith(".meta") else meta_path
            if not self._path_is_under(image_path, root_folder):
                return False
        return any_found

    def _map_source_to_target_image(self, source_meta_path, root_folder, use_jpg, path_cache):
        if not source_meta_path:
            return None
        source_image = source_meta_path[:-5] if source_meta_path.lower().endswith(
            ".meta") else source_meta_path
        cached = path_cache.get(source_image)
        if cached is not None:
            return cached
        if not self._path_is_under(source_image, root_folder):
            path_cache[source_image] = None
            return None
        rel = os.path.relpath(source_image, root_folder)
        base = os.path.splitext(rel)[0]
        exts = (".jpg", ".jpeg") if use_jpg else (".png",)
        for ext in exts:
            candidate = os.path.join(root_folder, base + ext)
            if os.path.isfile(candidate):
                path_cache[source_image] = candidate
                return candidate
        path_cache[source_image] = None
        return None

    def _resolve_target_sprite_name(self, source_name, name_to_id):
        if not name_to_id:
            return None
        if not source_name:
            return None
        if source_name and source_name in name_to_id:
            return source_name
        if source_name:
            add_n = re.sub(r"^(\d+)_", r"\1N_", source_name)
            if add_n != source_name and add_n in name_to_id:
                return add_n
            remove_n = re.sub(r"^(\d+)N_", r"\1_", source_name)
            if remove_n != source_name and remove_n in name_to_id:
                return remove_n
        if len(name_to_id) == 1:
            return next(iter(name_to_id))
        return None

    def _get_target_meta_data(self, image_path, meta_cache):
        if image_path in meta_cache:
            return meta_cache[image_path]
        meta_path = image_path + ".meta"
        if not os.path.isfile(meta_path):
            meta_cache[image_path] = (None, None)
            return None, None
        guid, name_to_id = self._parse_sprite_meta(meta_path)
        meta_cache[image_path] = (guid, name_to_id)
        return guid, name_to_id

    def _replace_sprite_library_auto_folder(self, source_path, target_path, root_folder, use_jpg, guid_to_fileid_name=None, guid_to_meta_path=None, guid_index_complete=False):
        if guid_to_fileid_name is None:
            guid_to_fileid_name = {}
        if guid_to_meta_path is None:
            guid_to_meta_path = {}

        categories, source_map, missing_source_map = self._extract_sprite_library_source_map(
            source_path,
            guid_to_fileid_name,
            guid_to_meta_path,
            guid_index_complete
        )
        if not categories:
            return {
                "mode": "auto",
                "updated": 0,
                "missing_atlas": 0,
                "missing_labels": 0,
                "missing_source_sprites": 0,
                "missing_atlas_categories": 0,
                "missing_target_categories": 0,
                "categories_total": 0,
                "categories_processed": 0,
            }

        with open(target_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            target_lines = f.readlines()
        target_index = self._index_sprite_library_categories(target_lines)
        target_map = {entry["norm"]: entry for entry in target_index}

        meta_cache = None
        name_index = None
        series_cache = {}
        total_updated = 0
        total_missing_atlas = 0
        total_missing_labels = 0
        total_missing_source_sprites = 0
        missing_atlas_categories = 0
        missing_target_categories = 0
        categories_processed = 0

        for category_name in categories:
            category_norm = self._normalize_entry_name(category_name)
            source_entry = source_map.get(category_norm)
            if not source_entry:
                continue

            label_to_sprite = source_entry["label_to_sprite"]
            label_to_meta = source_entry["label_to_meta"]
            total_missing_source_sprites += missing_source_map.get(
                category_norm, 0)
            target_entry = target_map.get(category_norm)
            if not target_entry:
                missing_target_categories += 1
                continue
            if self._label_paths_under_root(label_to_meta, root_folder):
                updated, missing_atlas, missing_labels = (
                    self._replace_sprite_library_category_from_source_by_path_lines(
                        target_lines,
                        target_entry["start"],
                        target_entry["end"],
                        label_to_sprite,
                        label_to_meta,
                        root_folder,
                        use_jpg
                    )
                )
                categories_processed += 1
                total_updated += updated
                total_missing_atlas += missing_atlas
                total_missing_labels += missing_labels
                continue

            sprite_names = {
                name for name in label_to_sprite.values() if name
            }
            if not sprite_names:
                continue

            if meta_cache is None:
                meta_cache, name_index = self._build_atlas_meta_index(
                    root_folder, use_jpg)
                if not meta_cache:
                    raise ValueError(
                        "No atlas .meta files found in the selected folder.")

            atlas_path = self._find_best_atlas_for_sprites(
                sprite_names, name_index
            )
            if not atlas_path:
                missing_atlas_categories += 1
                continue

            atlas_guid, name_to_id, atlas_series = self._get_cached_atlas_data(
                atlas_path, meta_cache, series_cache
            )
            if not atlas_guid or not name_to_id:
                missing_atlas_categories += 1
                continue

            updated, missing_atlas, missing_labels = (
                self._replace_sprite_library_category_from_source_lines(
                    target_lines,
                    target_entry["start"],
                    target_entry["end"],
                    label_to_sprite,
                    atlas_guid,
                    name_to_id,
                    atlas_series
                )
            )

            categories_processed += 1
            total_updated += updated
            total_missing_atlas += missing_atlas
            total_missing_labels += missing_labels

        if total_updated > 0:
            with open(target_path, "w", encoding="utf-8", newline="") as f:
                f.writelines(target_lines)

        return {
            "mode": "auto",
            "updated": total_updated,
            "missing_atlas": total_missing_atlas,
            "missing_labels": total_missing_labels,
            "missing_source_sprites": total_missing_source_sprites,
            "missing_atlas_categories": missing_atlas_categories,
            "missing_target_categories": missing_target_categories,
            "categories_total": len(categories),
            "categories_processed": categories_processed,
        }

    def _find_atlas_sprite_id(self, sprite_name, atlas_guid, name_to_id, atlas_series):
        if atlas_series:
            for atlas in atlas_series:
                file_id = atlas["name_to_id"].get(sprite_name)
                if file_id:
                    return file_id, atlas["guid"]
        file_id = name_to_id.get(sprite_name)
        if file_id:
            return file_id, atlas_guid
        return None, None

    def _replace_sprite_library_category_from_source_by_path_lines(self, lines, start, end, label_to_sprite, label_to_meta, root_folder, use_jpg):
        updated = 0
        missing_atlas = 0
        in_entries = False
        current_entry = None
        entry_target_id = None
        entry_atlas_guid = None
        entry_updated = False
        target_labels = set()
        target_meta_cache = {}
        target_path_cache = {}

        i = start
        while i < end:
            line = lines[i]
            stripped = line.strip()

            if stripped == "m_OverrideEntries:":
                in_entries = True
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_entries and line.startswith("    - m_Name: "):
                current_entry = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                target_labels.add(current_entry)
                entry_target_id = None
                entry_atlas_guid = None
                entry_updated = False
                sprite_name = label_to_sprite.get(current_entry)
                source_meta = label_to_meta.get(current_entry)
                if sprite_name and source_meta:
                    target_image = self._map_source_to_target_image(
                        source_meta, root_folder, use_jpg, target_path_cache)
                    if target_image:
                        atlas_guid, name_to_id = self._get_target_meta_data(
                            target_image, target_meta_cache)
                        if atlas_guid and name_to_id:
                            target_sprite = self._resolve_target_sprite_name(
                                sprite_name, name_to_id)
                            if target_sprite:
                                entry_target_id = name_to_id.get(
                                    target_sprite)
                                entry_atlas_guid = atlas_guid
                if sprite_name and entry_target_id is None:
                    missing_atlas += 1
                i += 1
                continue

            if in_entries and current_entry:
                key = None
                if stripped.startswith("m_Sprite:"):
                    key = "m_Sprite"
                elif stripped.startswith("m_SpriteOverride:"):
                    key = "m_SpriteOverride"

                if key:
                    if entry_target_id is None:
                        i += 1
                        continue

                    i = self._update_sprite_ref_line(
                        lines, i, key, entry_target_id, entry_atlas_guid)
                    if not entry_updated:
                        updated += 1
                        entry_updated = True
                    continue

            i += 1

        missing_labels = len(
            [label for label, sprite in label_to_sprite.items()
             if sprite and label not in target_labels]
        )

        return updated, missing_atlas, missing_labels

    def _replace_sprite_library_category_from_source_lines(self, lines, start, end, label_to_sprite, atlas_guid, name_to_id, atlas_series):
        updated = 0
        missing_atlas = 0
        in_entries = False
        current_entry = None
        entry_target_id = None
        entry_atlas_guid = atlas_guid
        entry_updated = False
        target_labels = set()

        i = start
        while i < end:
            line = lines[i]
            stripped = line.strip()

            if stripped == "m_OverrideEntries:":
                in_entries = True
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_entries and line.startswith("    - m_Name: "):
                current_entry = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                target_labels.add(current_entry)
                entry_target_id = None
                entry_atlas_guid = atlas_guid
                entry_updated = False
                sprite_name = label_to_sprite.get(current_entry)
                if sprite_name:
                    entry_target_id, entry_atlas_guid = self._find_atlas_sprite_id(
                        sprite_name, atlas_guid, name_to_id, atlas_series)
                    if entry_target_id is None:
                        missing_atlas += 1
                i += 1
                continue

            if in_entries and current_entry:
                key = None
                if stripped.startswith("m_Sprite:"):
                    key = "m_Sprite"
                elif stripped.startswith("m_SpriteOverride:"):
                    key = "m_SpriteOverride"

                if key:
                    if entry_target_id is None:
                        i += 1
                        continue

                    i = self._update_sprite_ref_line(
                        lines, i, key, entry_target_id, entry_atlas_guid)
                    if not entry_updated:
                        updated += 1
                        entry_updated = True
                    continue

            i += 1

        missing_labels = len(
            [label for label, sprite in label_to_sprite.items()
             if sprite and label not in target_labels]
        )

        return updated, missing_atlas, missing_labels

    def _replace_sprite_library_category_from_source_by_path(self, sprite_lib_path, category_name, label_to_sprite, label_to_meta, root_folder, use_jpg):
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        updated = 0
        missing_atlas = 0
        category_found = False
        in_library = False
        in_category = False
        in_entries = False
        current_entry = None
        entry_target_id = None
        entry_atlas_guid = None
        entry_updated = False
        target_labels = set()
        normalized_target = self._normalize_entry_name(category_name)
        target_meta_cache = {}
        target_path_cache = {}

        i = 0
        while i < len(lines):
            line = lines[i]
            stripped = line.strip()

            if stripped == "m_Library:":
                in_library = True
                in_category = False
                in_entries = False
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_library and line.startswith("  - m_Name: "):
                current_category = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                in_category = (current_category == normalized_target)
                if in_category:
                    category_found = True
                in_entries = False
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_category and stripped == "m_OverrideEntries:":
                in_entries = True
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_category and in_entries and line.startswith("    - m_Name: "):
                current_entry = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                target_labels.add(current_entry)
                entry_target_id = None
                entry_atlas_guid = None
                entry_updated = False
                sprite_name = label_to_sprite.get(current_entry)
                source_meta = label_to_meta.get(current_entry)
                if sprite_name and source_meta:
                    target_image = self._map_source_to_target_image(
                        source_meta, root_folder, use_jpg, target_path_cache)
                    if target_image:
                        atlas_guid, name_to_id = self._get_target_meta_data(
                            target_image, target_meta_cache)
                        if atlas_guid and name_to_id:
                            target_sprite = self._resolve_target_sprite_name(
                                sprite_name, name_to_id)
                            if target_sprite:
                                entry_target_id = name_to_id.get(
                                    target_sprite)
                                entry_atlas_guid = atlas_guid
                if sprite_name and entry_target_id is None:
                    missing_atlas += 1
                i += 1
                continue

            if in_category and in_entries and current_entry:
                key = None
                if stripped.startswith("m_Sprite:"):
                    key = "m_Sprite"
                elif stripped.startswith("m_SpriteOverride:"):
                    key = "m_SpriteOverride"

                if key:
                    if entry_target_id is None:
                        i += 1
                        continue

                    i = self._update_sprite_ref_line(
                        lines, i, key, entry_target_id, entry_atlas_guid)
                    if not entry_updated:
                        updated += 1
                        entry_updated = True
                    continue

            i += 1

        missing_labels = len(
            [label for label, sprite in label_to_sprite.items()
             if sprite and label not in target_labels]
        )

        if category_found and updated > 0:
            with open(sprite_lib_path, "w", encoding="utf-8", newline="") as f:
                f.writelines(lines)

        return updated, missing_atlas, missing_labels, category_found

    def _replace_sprite_library_category_sequential(self, sprite_lib_path, category_name, sprite_sequence):
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        updated = 0
        missing_atlas = 0
        missing_labels = 0
        category_found = False
        in_library = False
        in_category = False
        in_entries = False
        current_entry = None
        entry_target_id = None
        entry_atlas_guid = None
        entry_updated = False
        normalized_target = self._normalize_entry_name(category_name)
        sprite_index = 0

        i = 0
        while i < len(lines):
            line = lines[i]
            stripped = line.strip()

            if stripped == "m_Library:":
                in_library = True
                in_category = False
                in_entries = False
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_library and line.startswith("  - m_Name: "):
                current_category = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                in_category = (current_category == normalized_target)
                if in_category:
                    category_found = True
                in_entries = False
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_category and stripped == "m_OverrideEntries:":
                in_entries = True
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_category and in_entries and line.startswith("    - m_Name: "):
                current_entry = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                entry_target_id = None
                entry_atlas_guid = None
                entry_updated = False
                if sprite_index < len(sprite_sequence):
                    entry_target_id, entry_atlas_guid = sprite_sequence[sprite_index]
                    sprite_index += 1
                else:
                    missing_atlas += 1
                i += 1
                continue

            if in_category and in_entries and current_entry:
                key = None
                if stripped.startswith("m_Sprite:"):
                    key = "m_Sprite"
                elif stripped.startswith("m_SpriteOverride:"):
                    key = "m_SpriteOverride"

                if key:
                    if entry_target_id is None:
                        i += 1
                        continue

                    i = self._update_sprite_ref_line(
                        lines, i, key, entry_target_id, entry_atlas_guid)
                    if not entry_updated:
                        updated += 1
                        entry_updated = True
                    continue

            i += 1

        if category_found and updated > 0:
            with open(sprite_lib_path, "w", encoding="utf-8", newline="") as f:
                f.writelines(lines)

        return updated, missing_atlas, missing_labels, category_found

    def _replace_sprite_library_category_from_source(self, sprite_lib_path, category_name, label_to_sprite, atlas_guid, name_to_id, atlas_series):
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        updated = 0
        missing_atlas = 0
        category_found = False
        in_library = False
        in_category = False
        in_entries = False
        current_entry = None
        entry_target_id = None
        entry_atlas_guid = atlas_guid
        entry_updated = False
        target_labels = set()
        normalized_target = self._normalize_entry_name(category_name)

        i = 0
        while i < len(lines):
            line = lines[i]
            stripped = line.strip()

            if stripped == "m_Library:":
                in_library = True
                in_category = False
                in_entries = False
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_library and line.startswith("  - m_Name: "):
                current_category = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                in_category = (current_category == normalized_target)
                if in_category:
                    category_found = True
                in_entries = False
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_category and stripped == "m_OverrideEntries:":
                in_entries = True
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_category and in_entries and line.startswith("    - m_Name: "):
                current_entry = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                target_labels.add(current_entry)
                entry_target_id = None
                entry_atlas_guid = atlas_guid
                entry_updated = False
                sprite_name = label_to_sprite.get(current_entry)
                if sprite_name:
                    entry_target_id, entry_atlas_guid = self._find_atlas_sprite_id(
                        sprite_name, atlas_guid, name_to_id, atlas_series)
                    if entry_target_id is None:
                        missing_atlas += 1
                i += 1
                continue

            if in_category and in_entries and current_entry:
                key = None
                if stripped.startswith("m_Sprite:"):
                    key = "m_Sprite"
                elif stripped.startswith("m_SpriteOverride:"):
                    key = "m_SpriteOverride"

                if key:
                    if entry_target_id is None:
                        i += 1
                        continue

                    i = self._update_sprite_ref_line(
                        lines, i, key, entry_target_id, entry_atlas_guid)
                    if not entry_updated:
                        updated += 1
                        entry_updated = True
                    continue

            i += 1

        missing_labels = len(
            [label for label, sprite in label_to_sprite.items()
             if sprite and label not in target_labels]
        )

        if category_found and updated > 0:
            with open(sprite_lib_path, "w", encoding="utf-8", newline="") as f:
                f.writelines(lines)

        return updated, missing_atlas, missing_labels, category_found

    def _parse_sprite_meta(self, meta_path):
        with open(meta_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        guid = None
        for line in lines:
            stripped = line.strip()
            if stripped.startswith("guid:"):
                guid = stripped.split(":", 1)[1].strip()
                break

        name_file_table = self._parse_name_file_id_table(lines)
        sheet_table = self._parse_sprite_sheet_table(lines)

        name_to_id = {}
        if sheet_table:
            name_to_id.update(sheet_table)
        if name_file_table:
            for name, file_id in name_file_table.items():
                if name not in name_to_id:
                    name_to_id[name] = file_id

        return guid, name_to_id

    def _load_sprite_sheet_entries(self, meta_path):
        with open(meta_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        guid = None
        for line in lines:
            stripped = line.strip()
            if stripped.startswith("guid:"):
                guid = stripped.split(":", 1)[1].strip()
                break

        entries = self._parse_sprite_sheet_entries(lines)
        if entries:
            entries = self._sort_sprite_sheet_entries(entries)
        return guid, entries

    def _parse_sprite_sheet_entries(self, lines):
        entries = []
        in_sheet = False
        sheet_indent = 0
        in_sprites = False
        sprites_indent = 0
        in_item = False
        current_name = None
        current_id = None

        def commit():
            if current_name is not None and current_id is not None:
                entries.append((current_name, current_id))

        for line in lines:
            stripped = line.strip()
            if stripped.startswith("spriteSheet:"):
                in_sheet = True
                sheet_indent = len(line) - len(line.lstrip())
                in_sprites = False
                in_item = False
                current_name = None
                current_id = None
                continue
            if not in_sheet:
                continue
            if stripped == "":
                continue
            indent = len(line) - len(line.lstrip())
            if indent <= sheet_indent:
                if in_item:
                    commit()
                break
            if stripped.startswith("sprites:"):
                in_sprites = True
                sprites_indent = indent
                in_item = False
                current_name = None
                current_id = None
                continue
            if not in_sprites:
                continue
            if indent < sprites_indent:
                if in_item:
                    commit()
                in_sprites = False
                in_item = False
                current_name = None
                current_id = None
                continue
            if indent == sprites_indent:
                if stripped.startswith("-"):
                    if in_item:
                        commit()
                    in_item = True
                    current_name = None
                    current_id = None
                    if stripped.startswith("- name:"):
                        current_name = self._normalize_entry_name(
                            stripped.split(":", 1)[1].strip())
                    continue
                if in_item:
                    commit()
                in_sprites = False
                in_item = False
                current_name = None
                current_id = None
                continue
            if not in_item:
                continue
            if stripped.startswith("name:"):
                current_name = self._normalize_entry_name(
                    stripped.split(":", 1)[1].strip())
                continue
            if stripped.startswith("internalID:"):
                current_id = stripped.split(":", 1)[1].strip()
                continue

        if in_item:
            commit()
        return entries

    def _sort_sprite_sheet_entries(self, entries):
        indexed = []
        leftover = []
        for i, (name, file_id) in enumerate(entries):
            idx = self._sprite_sheet_entry_index(name)
            if idx is None:
                leftover.append((i, name, file_id))
            else:
                indexed.append((idx, i, name, file_id))
        indexed.sort(key=lambda item: (item[0], item[1]))
        ordered = [(name, file_id) for _, _, name, file_id in indexed]
        ordered.extend((name, file_id) for _, name, file_id in leftover)
        return ordered

    def _sprite_sheet_entry_index(self, name):
        if not name:
            return None
        parts = name.split("_")
        for part in reversed(parts):
            if part.isdigit():
                return int(part)
        match = re.search(r"(\d+)$", name)
        if match:
            return int(match.group(1))
        return None

    def _build_sprite_sequence_from_series(self, atlas_series):
        sprite_sequence = []
        for atlas in atlas_series:
            guid = atlas["guid"]
            _, sprite_entries = self._load_sprite_sheet_entries(atlas["meta"])
            if not sprite_entries:
                continue
            sprite_sequence.extend(
                (file_id, guid) for _, file_id in sprite_entries
            )
        return sprite_sequence

    def _parse_internal_id_table(self, lines):
        name_to_id = {}
        in_table = False
        table_indent = 0
        pending_id = None

        for line in lines:
            stripped = line.strip()
            if stripped.startswith("internalIDToNameTable:"):
                in_table = True
                table_indent = len(line) - len(line.lstrip())
                pending_id = None
                continue
            if not in_table:
                continue
            if stripped == "":
                continue
            indent = len(line) - len(line.lstrip())
            if indent <= table_indent and not stripped.startswith("-"):
                break
            if stripped.startswith("213:"):
                pending_id = stripped.split(":", 1)[1].strip()
                continue
            if stripped.startswith("second:"):
                name = stripped.split(":", 1)[1].strip()
                if pending_id is not None:
                    name_to_id[name] = pending_id
                    pending_id = None

        return name_to_id

    def _parse_name_file_id_table(self, lines):
        name_to_id = {}
        in_table = False
        table_indent = 0

        for line in lines:
            stripped = line.strip()
            if stripped.startswith("nameFileIdTable:"):
                in_table = True
                table_indent = len(line) - len(line.lstrip())
                continue
            if not in_table:
                continue
            if stripped == "":
                continue
            indent = len(line) - len(line.lstrip())
            if indent <= table_indent:
                break
            if ":" in stripped:
                name, value = stripped.split(":", 1)
                name_to_id[name.strip()] = value.strip()

        return name_to_id

    def _parse_sprite_sheet_table(self, lines):
        name_to_id = {}
        in_sheet = False
        sheet_indent = 0
        in_sprites = False
        sprites_indent = 0
        in_item = False
        current_name = None
        pending_id = None

        for line in lines:
            stripped = line.strip()
            if stripped.startswith("spriteSheet:"):
                in_sheet = True
                sheet_indent = len(line) - len(line.lstrip())
                in_sprites = False
                in_item = False
                current_name = None
                pending_id = None
                continue
            if not in_sheet:
                continue
            if stripped == "":
                continue
            indent = len(line) - len(line.lstrip())
            if indent <= sheet_indent:
                break
            if stripped.startswith("sprites:"):
                in_sprites = True
                sprites_indent = indent
                in_item = False
                current_name = None
                pending_id = None
                continue
            if not in_sprites:
                continue
            if indent < sprites_indent:
                in_sprites = False
                in_item = False
                current_name = None
                pending_id = None
                continue
            if indent == sprites_indent:
                if stripped.startswith("-"):
                    in_item = True
                    current_name = None
                    pending_id = None
                    if stripped.startswith("- name:"):
                        current_name = self._normalize_entry_name(
                            stripped.split(":", 1)[1].strip())
                    continue
                in_sprites = False
                in_item = False
                current_name = None
                pending_id = None
                continue
            if not in_item:
                continue
            if stripped.startswith("name:"):
                current_name = self._normalize_entry_name(
                    stripped.split(":", 1)[1].strip())
                if pending_id is not None:
                    name_to_id[current_name] = pending_id
                    current_name = None
                    pending_id = None
                continue
            if stripped.startswith("internalID:"):
                value = stripped.split(":", 1)[1].strip()
                if current_name:
                    name_to_id[current_name] = value
                    current_name = None
                else:
                    pending_id = value

        return name_to_id

    def _renumber_sprite_library_category(self, sprite_lib_path, category_name, prefix, suffix):
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        updated = 0
        category_found = False
        in_library = False
        in_category = False
        in_entries = False
        label_index = 1
        normalized_target = self._normalize_entry_name(category_name)

        for i, line in enumerate(lines):
            stripped = line.strip()

            if stripped == "m_Library:":
                in_library = True
                in_category = False
                in_entries = False
                continue

            if in_library and line.startswith("  - m_Name: "):
                current_category = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                in_category = (current_category == normalized_target)
                if in_category:
                    category_found = True
                    label_index = 1
                in_entries = False
                continue

            if in_category and stripped == "m_OverrideEntries:":
                in_entries = True
                continue

            if in_category and in_entries and line.startswith("    - m_Name: "):
                new_label = f"{prefix}{label_index}{suffix}"
                lines[i] = self._replace_sprite_label_line(line, new_label)
                updated += 1
                label_index += 1

        if category_found and updated > 0:
            with open(sprite_lib_path, "w", encoding="utf-8", newline="") as f:
                f.writelines(lines)

        return updated, category_found

    def _replace_sprite_library_category(self, sprite_lib_path, category_name, atlas_guid, name_to_id, atlas_base, atlas_series):
        with open(sprite_lib_path, "r", encoding="utf-8", errors="replace", newline="") as f:
            lines = f.readlines()

        updated = 0
        missing = 0
        category_found = False
        in_library = False
        in_category = False
        in_entries = False
        current_entry = None
        entry_target_id = None
        entry_atlas_guid = atlas_guid
        entry_missing = False
        entry_updated = False
        assets_root = self._find_assets_root(sprite_lib_path)
        guid_to_fileid_name = {}

        i = 0
        while i < len(lines):
            line = lines[i]
            stripped = line.strip()

            if stripped == "m_Library:":
                in_library = True
                in_category = False
                in_entries = False
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_library and line.startswith("  - m_Name: "):
                current_category = line.split(":", 1)[1].strip()
                in_category = (current_category == category_name)
                if in_category:
                    category_found = True
                in_entries = False
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_category and stripped == "m_OverrideEntries:":
                in_entries = True
                current_entry = None
                entry_updated = False
                i += 1
                continue

            if in_category and in_entries and line.startswith("    - m_Name: "):
                current_entry = self._normalize_entry_name(
                    line.split(":", 1)[1].strip())
                entry_target_id = None
                entry_atlas_guid = atlas_guid
                if current_entry.isdigit() and atlas_series:
                    atlas_match, local_index = self._map_label_to_atlas(
                        int(current_entry), atlas_series)
                    if atlas_match:
                        entry_atlas_guid = atlas_match["guid"]
                        entry_target_id = self._match_entry_name_to_id(
                            str(local_index), atlas_match["name_to_id"], atlas_match["base"])
                        if entry_target_id is None:
                            entry_target_id = self._match_entry_name_to_id(
                                current_entry, atlas_match["name_to_id"], atlas_match["base"])

                if entry_target_id is None:
                    entry_target_id = self._match_entry_name_to_id(
                        current_entry, name_to_id, atlas_base)
                entry_missing = False
                entry_updated = False
                i += 1
                continue

            if in_category and in_entries and current_entry:
                key = None
                if stripped.startswith("m_Sprite:"):
                    key = "m_Sprite"
                elif stripped.startswith("m_SpriteOverride:"):
                    key = "m_SpriteOverride"

                if key:
                    if entry_target_id is None:
                        old_file_id, old_guid = self._parse_sprite_ref_line(
                            line)
                        if old_file_id and old_guid:
                            fileid_to_name = guid_to_fileid_name.get(old_guid)
                            if fileid_to_name is None:
                                meta_path = self._find_meta_by_guid(
                                    assets_root, old_guid)
                                if meta_path:
                                    fileid_to_name = self._build_fileid_to_name(
                                        meta_path)
                                else:
                                    fileid_to_name = {}
                                guid_to_fileid_name[old_guid] = fileid_to_name
                            sprite_name = fileid_to_name.get(old_file_id)
                            if sprite_name:
                                if atlas_series:
                                    for atlas in atlas_series:
                                        if sprite_name in atlas["name_to_id"]:
                                            entry_target_id = atlas["name_to_id"][sprite_name]
                                            entry_atlas_guid = atlas["guid"]
                                            break
                                if entry_target_id is None:
                                    entry_target_id = name_to_id.get(
                                        sprite_name)

                    if entry_target_id is None:
                        if not entry_missing:
                            missing += 1
                            entry_missing = True
                        i += 1
                        continue

                    i = self._update_sprite_ref_line(
                        lines, i, key, entry_target_id, entry_atlas_guid)
                    if not entry_updated:
                        updated += 1
                        entry_updated = True
                    continue

            i += 1

        if category_found and updated > 0:
            with open(sprite_lib_path, "w", encoding="utf-8", newline="") as f:
                f.writelines(lines)

        return updated, missing, category_found

    def _update_sprite_ref_line(self, lines, index, key, file_id, guid):
        line = lines[index]
        base, ending = self._split_line_ending(line)
        indent = base[:len(base) - len(base.lstrip())]

        if "type:" in base:
            lines[index] = (
                f"{indent}{key}: {{fileID: {file_id}, guid: {guid}, type: 3}}{ending}"
            )
            return index + 1

        if index + 1 < len(lines):
            next_line = lines[index + 1]
            next_base, next_ending = self._split_line_ending(next_line)
            if next_base.strip().startswith("type:"):
                next_indent = next_base[:len(
                    next_base) - len(next_base.lstrip())]
                lines[index] = f"{indent}{key}: {{fileID: {file_id}, guid: {guid},{ending}"
                lines[index + 1] = f"{next_indent}type: 3}}{next_ending}"
                return index + 2

        lines[index] = (
            f"{indent}{key}: {{fileID: {file_id}, guid: {guid}, type: 3}}{ending}"
        )
        return index + 1

    def _split_line_ending(self, line):
        if line.endswith("\r\n"):
            return line[:-2], "\r\n"
        if line.endswith("\n"):
            return line[:-1], "\n"
        return line, ""

    def _normalize_entry_name(self, name):
        if name.startswith("\"") and name.endswith("\"") and len(name) >= 2:
            name = name[1:-1]
        return name.strip()

    def _replace_sprite_label_line(self, line, new_label):
        base, ending = self._split_line_ending(line)
        if "m_Name:" not in base:
            return line
        prefix, name_part = base.split("m_Name:", 1)
        name_part = name_part.strip()
        formatted = self._format_sprite_label(new_label, name_part)
        return f"{prefix}m_Name: {formatted}{ending}"

    def _format_sprite_label(self, label, existing_label):
        if existing_label.startswith("\"") and existing_label.endswith("\""):
            return self._quote_yaml_double(label)
        if existing_label.startswith("'") and existing_label.endswith("'"):
            return self._quote_yaml_single(label)
        if self._sprite_label_needs_quotes(label):
            return self._quote_yaml_double(label)
        return label

    def _sprite_label_needs_quotes(self, label):
        if label == "" or label != label.strip():
            return True
        if label.startswith(("-", "?", "!", "&", "*", "@")):
            return True
        if any(ch in label for ch in (":", "#", "\n", "\r", "\t")):
            return True
        if label.lower() in ("null", "true", "false", "yes", "no", "on", "off", "~"):
            return True
        return False

    def _quote_yaml_double(self, value):
        escaped = value.replace("\\", "\\\\").replace("\"", "\\\"")
        return f"\"{escaped}\""

    def _quote_yaml_single(self, value):
        escaped = value.replace("'", "''")
        return f"'{escaped}'"

    def _match_entry_name_to_id(self, entry_name, name_to_id, atlas_base):
        if entry_name in name_to_id:
            return name_to_id[entry_name]

        if entry_name.isdigit():
            base = (atlas_base or "").strip()
            if base:
                candidate = f"{base}_{entry_name}"
                if candidate in name_to_id:
                    return name_to_id[candidate]
                candidate = f"{base}{entry_name}"
                if candidate in name_to_id:
                    return name_to_id[candidate]

            suffixes = (f"_{entry_name}", f"-{entry_name}", f" {entry_name}")
            suffix_matches = [
                name for name in name_to_id if name.endswith(suffixes)
            ]
            if len(suffix_matches) == 1:
                return name_to_id[suffix_matches[0]]

            stripped = entry_name.lstrip("0") or "0"
            digit_suffix_matches = []
            for name in name_to_id:
                tail = name.rsplit("_", 1)[-1]
                if tail.isdigit() and tail.lstrip("0") == stripped:
                    digit_suffix_matches.append(name)
            if len(digit_suffix_matches) == 1:
                return name_to_id[digit_suffix_matches[0]]

        return None

    def _build_atlas_series(self, atlas_file):
        path = Path(atlas_file)
        stem = path.stem
        match = re.match(r"^(.*?)(\d+)$", stem)
        if not match:
            return None
        prefix, num_str = match.groups()
        num = int(num_str)
        width = len(num_str)
        ext = path.suffix
        folder = path.parent
        series = []
        while True:
            if width > 1:
                candidate_stem = f"{prefix}{num:0{width}d}"
            else:
                candidate_stem = f"{prefix}{num}"
            candidate = folder / f"{candidate_stem}{ext}"
            if not candidate.exists():
                break
            meta_path = str(candidate) + ".meta"
            if not os.path.isfile(meta_path):
                raise FileNotFoundError(f"Atlas .meta not found:\n{meta_path}")
            guid, name_to_id = self._parse_sprite_meta(meta_path)
            if not guid:
                raise ValueError(f"Atlas .meta missing guid:\n{meta_path}")
            count = self._count_numeric_suffixes(name_to_id, candidate_stem)
            series.append({
                "num": num,
                "base": candidate_stem,
                "path": str(candidate),
                "meta": meta_path,
                "guid": guid,
                "name_to_id": name_to_id,
                "count": count,
            })
            num += 1
        return series or None

    def _count_numeric_suffixes(self, name_to_id, base):
        suffixes = set()
        if base:
            base_re = re.compile(rf"^{re.escape(base)}[_-]?(\d+)$")
            for name in name_to_id:
                match = base_re.match(name)
                if match:
                    suffix = int(match.group(1))
                    if suffix > 0:
                        suffixes.add(suffix)
        if suffixes:
            return len(suffixes)

        for name in name_to_id:
            match = re.search(r"(\d+)$", name)
            if match:
                suffix = int(match.group(1))
                if suffix > 0:
                    suffixes.add(suffix)
        return len(suffixes) if suffixes else len(name_to_id)

    def _map_label_to_atlas(self, label, atlas_series):
        remaining = label
        for atlas in atlas_series:
            count = atlas.get("count") or 0
            if count <= 0:
                continue
            if remaining <= count:
                return atlas, remaining
            remaining -= count
        return None, None

    def _find_assets_root(self, asset_path):
        p = Path(asset_path)
        parts = p.parts
        for i, part in enumerate(parts):
            if part.lower() == "assets":
                return str(Path(*parts[:i + 1]))
        return str(p.parent)

    def _build_guid_to_meta_index(self, assets_root):
        guid_to_meta = {}
        meta_paths = list(iter_files(assets_root, {".meta"}))

        def process(path):
            try:
                with open(path, "r", encoding="utf-8", errors="replace") as f:
                    for _ in range(20):
                        line = f.readline()
                        if not line:
                            break
                        stripped = line.strip()
                        if stripped.startswith("guid:"):
                            guid = stripped.split(":", 1)[1].strip()
                            if guid:
                                return guid, path
                            break
            except Exception:
                return None
            return None

        results = self._parallel_map(meta_paths, process, self._io_workers)
        for item in results:
            if not item:
                continue
            guid, path = item
            if guid not in guid_to_meta:
                guid_to_meta[guid] = path
        return guid_to_meta

    def _find_meta_by_guid(self, assets_root, guid):
        target = f"guid: {guid}"
        for path in iter_files(assets_root, {".meta"}):
            try:
                with open(path, "r", encoding="utf-8", errors="replace") as f:
                    for _ in range(10):
                        line = f.readline()
                        if not line:
                            break
                        if line.strip() == target:
                            return path
            except Exception:
                continue
        return None

    def _build_fileid_to_name(self, meta_path):
        _, name_to_id = self._parse_sprite_meta(meta_path)
        if not name_to_id:
            return {}
        fileid_to_name = {}
        for name, file_id in name_to_id.items():
            if file_id not in fileid_to_name:
                fileid_to_name[file_id] = name
        return fileid_to_name

    def _parse_sprite_ref_line(self, line):
        if "fileID:" not in line or "guid:" not in line or "{" not in line:
            return None, None
        try:
            chunk = line.split("{", 1)[1].replace("}", "")
        except Exception:
            return None, None
        file_id = None
        guid = None
        for part in chunk.split(","):
            part = part.strip()
            if part.startswith("fileID:"):
                file_id = part.split(":", 1)[1].strip()
            elif part.startswith("guid:"):
                guid = part.split(":", 1)[1].strip()
        return file_id, guid


def main():
    r = tk.Tk()
    AA(r)
    r.mainloop()


if __name__ == "__main__":
    main()
