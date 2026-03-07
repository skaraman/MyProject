import tkinter as tk
from tkinter import filedialog, messagebox
import re
import random
import os

# ---------------------------
# Obfuscation Logic
# ---------------------------

def random_name():
    return "x" + str(random.randint(1000, 9999))


def obfuscate(code):

    code = re.sub(r'/\*.*?\*/', '', code, flags=re.S)
    code = re.sub(r'//.*', '', code)
    code = re.sub(r'\s+', ' ', code)

    class_map = {}
    method_map = {}
    var_map = {}

    # Classes
    classes = re.findall(r'\bclass\s+([A-Za-z_]\w*)', code)
    for c in classes:
        if c not in class_map:
            class_map[c] = random_name()

    for k, v in class_map.items():
        code = re.sub(rf'\b{k}\b', v, code)

    # Methods
    methods = re.findall(
        r'\b(?:public|private|protected|internal|static|virtual|void|int|string|bool|double|float|char|decimal|long|short)\s+([A-Za-z_]\w*)\s*\(',
        code
    )

    for m in methods:
        if m not in method_map and m not in class_map:
            method_map[m] = random_name()

    for k, v in method_map.items():
        code = re.sub(rf'\b{k}\b', v, code)

    # Variables
    vars_found = re.findall(
        r'\b(?:int|float|double|string|bool|var|char|decimal|long|short)\s+([A-Za-z_]\w*)',
        code
    )

    for v in vars_found:
        if v not in var_map and v not in method_map and v not in class_map:
            var_map[v] = random_name()

    for k, v in var_map.items():
        code = re.sub(rf'\b{k}\b', v, code)

    code = re.sub(r'\s*([{}();,+\-*/=<>])\s*', r'\1', code)

    return code, class_map, method_map, var_map


# ---------------------------
# GUI
# ---------------------------

class ObfuscatorGUI:

    def __init__(self, root):

        self.root = root
        self.root.title("C# Obfuscator")
        self.root.geometry("600x350")

        # Dark theme colors
        bg = "#1e1e1e"
        fg = "#ffffff"
        accent = "#3a7ff6"

        root.configure(bg=bg)

        self.input_path = tk.StringVar()
        self.output_path = tk.StringVar()

        # Input file
        tk.Label(root, text="Input C# File", bg=bg, fg=fg).pack(pady=(15,0))

        frame1 = tk.Frame(root, bg=bg)
        frame1.pack()

        tk.Entry(frame1, textvariable=self.input_path, width=55, bg="#2d2d2d", fg=fg, insertbackground=fg).pack(side=tk.LEFT, padx=5)

        tk.Button(frame1, text="Browse", bg=accent, fg="white",
                  command=self.browse_input).pack(side=tk.LEFT)

        # Output file
        tk.Label(root, text="Output File", bg=bg, fg=fg).pack(pady=(15,0))

        frame2 = tk.Frame(root, bg=bg)
        frame2.pack()

        tk.Entry(frame2, textvariable=self.output_path, width=55, bg="#2d2d2d", fg=fg, insertbackground=fg).pack(side=tk.LEFT, padx=5)

        tk.Button(frame2, text="Browse", bg=accent, fg="white",
                  command=self.browse_output).pack(side=tk.LEFT)

        # Run button
        tk.Button(root,
                  text="Obfuscate",
                  bg=accent,
                  fg="white",
                  width=20,
                  height=2,
                  command=self.run_obfuscation).pack(pady=20)

        # Console log
        self.console = tk.Text(root,
                               height=8,
                               bg="#121212",
                               fg="#00ff9c",
                               insertbackground="white")
        self.console.pack(fill="both", padx=10, pady=10)

    def browse_input(self):
        path = filedialog.askopenfilename(filetypes=[("C# Files","*.cs")])
        if path:
            self.input_path.set(path)

    def browse_output(self):
        path = filedialog.asksaveasfilename(defaultextension=".cs")
        if path:
            self.output_path.set(path)

    def log(self, text):
        self.console.insert(tk.END, text + "\n")
        self.console.see(tk.END)

    def run_obfuscation(self):

        inp = self.input_path.get()
        out = self.output_path.get()

        if not os.path.exists(inp):
            messagebox.showerror("Error","Input file not found")
            return

        try:
            with open(inp,"r",encoding="utf-8") as f:
                code = f.read()

            obf, c_map, m_map, v_map = obfuscate(code)

            with open(out,"w",encoding="utf-8") as f:
                f.write(obf)

            self.log("✔ Obfuscation complete")
            self.log(f"Classes renamed: {len(c_map)}")
            self.log(f"Methods renamed: {len(m_map)}")
            self.log(f"Variables renamed: {len(v_map)}")

        except Exception as e:
            messagebox.showerror("Error", str(e))


# ---------------------------
# Start App
# ---------------------------

root = tk.Tk()
app = ObfuscatorGUI(root)
root.mainloop()