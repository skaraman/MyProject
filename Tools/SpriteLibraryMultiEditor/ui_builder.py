# UI Builder module for Sprite Library Multi-Editor
"""Build and configure the Tkinter GUI components."""

import tkinter as tk
import tkinter.font as tkfont
import tkinter.messagebox as messagebox
import tkinter.ttk as ttk
import sys


# Dark theme colors - deep dark palette with blue tint
DARK_THEME = {
    # Deep midnight blue base tones
    "bg": "#06090F",
    "fg": "#B8C5E6",
    "frame_bg": "#0A1224",
    "panel_bg": "#070C17",
    "selected_bg": "#131D32",
    "selected_fg": "#FFFFFF",
    "border_bg": "#020305",  # Nearly invisible border
    "highlight_bg": "#1E3A5F",  # Deep blue highlight
    "disabled_fg": "#4A5678",
    "button_bg": "#131D32",
    "button_fg": "#D8E2F5",
    "button_hover_bg": "#1B2C49",
    "button_active_bg": "#0F2944",
    "tree_header_bg": "#0D162A",
    "scrollbar_bg": "#131D32",
    "scrollbar_fg": "#5A7BA8",
    "menu_bg": "#0A1224",
    "menu_fg": "#D8E2F5",
    "menu_hover_bg": "#1E3A5F",
}

# Border and shadow colors for depth
BORDER_COLORS = {
    "inner_light": "#0C162E",  # Subtle inner highlight
    "inner_dark": "#000000",   # Deep shadow
    "divider": "#030508",      # Panel dividers
}

# Window decoration colors (for custom title bar)
WINDOW_DECORATION = {
    "title_bar_bg": "#0A1224",
    "title_bar_text": "#B8C5E6",
    "title_bar_border_top": "#1E3A5F",  # Blue accent at top
    "close_button_bg": "#131D32",
    "close_button_hover": "#1B2C49",
}


DEFAULT_UI_SCALE = 1.0
MIN_UI_SCALE = 0.75
MAX_UI_SCALE = 1.75
UI_SCALE_STEP = 0.1


def clamp_ui_scale(scale: float) -> float:
    scale = max(MIN_UI_SCALE, scale)
    scale = min(MAX_UI_SCALE, scale)

    return round(scale, 2)


def apply_ui_scale(root: tk.Tk, scale: float) -> float:
    scale = clamp_ui_scale(scale)
    _cache_base_widget_fonts(root)
    root.tk.call("tk", "scaling", scale)
    _apply_scaled_ttk_styles(scale)
    _apply_scaled_widget_fonts(root, scale)

    return scale


def _cache_base_widget_fonts(widget: tk.Widget) -> None:
    _cache_base_widget_font(widget)

    for child in widget.winfo_children():
        _cache_base_widget_fonts(child)


def _cache_base_widget_font(widget: tk.Widget) -> None:
    if hasattr(widget, "_base_font"):
        return

    try:
        current_font = widget.cget("font")
    except tk.TclError:
        return

    if not current_font:
        return

    actual = tkfont.Font(root=widget, font=current_font).actual()
    family = actual["family"]
    size = abs(actual["size"])
    weight = actual["weight"]

    widget._base_font = (family, size, weight)


def _apply_scaled_widget_fonts(widget: tk.Widget, scale: float) -> None:
    _apply_scaled_widget_font(widget, scale)

    for child in widget.winfo_children():
        _apply_scaled_widget_fonts(child, scale)


def _apply_scaled_widget_font(widget: tk.Widget, scale: float) -> None:
    base_font = getattr(widget, "_base_font", None)

    if base_font is None:
        return

    family, base_size, weight = base_font
    size = _scaled_size(base_size, scale)
    font = (family, size, weight)

    try:
        widget.configure(font=font)
    except tk.TclError:
        return

    if isinstance(widget, ttk.Treeview):
        widget.tag_configure("doc_header", font=("Segoe UI", size, "bold"))


def _scaled_size(base_size: int, scale: float) -> int:
    size = round(base_size * scale)

    return max(1, size)


def _apply_scaled_ttk_styles(scale: float) -> None:
    style = ttk.Style()

    base_font_size = _scaled_size(9, scale)
    header_font_size = _scaled_size(9, scale)
    row_height = _scaled_size(22, scale)
    button_x = _scaled_size(10, scale)
    button_y = _scaled_size(4, scale)
    arrow_size = _scaled_size(12, scale)

    style.configure(".", font=("Segoe UI", base_font_size))
    style.configure("TButton", padding=(button_x, button_y))
    style.configure("Treeview", rowheight=row_height)
    style.configure("Treeview.Heading", font=("Segoe UI", header_font_size, "bold"))
    style.configure("Vertical.TScrollbar", arrowsize=arrow_size)


def create_main_window(title: str = "Sprite Library Multi-Editor") -> tk.Tk:
    """Create and configure the main application window with dark theme."""
    root = tk.Tk()
    root.title(title)
    root.minsize(1024, 768)
    _apply_dark_theme(root)
    return root


def _apply_dark_theme(root: tk.Tk) -> None:
    """Apply dark theme colors and ttk style to the root window."""
    # Configure base Tkinter widget defaults
    root.option_add("*Font", ("Segoe UI", 9))
    root.option_add("*Label.Font", ("Segoe UI", 9))
    root.option_add("*Button.Font", ("Segoe UI", 9))

    # Set default colors for non-ttk widgets
    root.configure(bg=DARK_THEME["bg"])
    _apply_windows_dark_title_bar(root)
    root.tk_setPalette(
        background=DARK_THEME["frame_bg"],
        foreground=DARK_THEME["fg"],
        activeBackground=DARK_THEME["button_hover_bg"],
        activeForeground=DARK_THEME["selected_fg"],
        highlightColor=DARK_THEME["highlight_bg"],
        highlightBackground=DARK_THEME["border_bg"],
        selectBackground=DARK_THEME["highlight_bg"],
        selectForeground=DARK_THEME["selected_fg"],
    )

    # Anti-aliasing and visual quality settings
    apply_ui_scale(root, DEFAULT_UI_SCALE)
    root.option_add("*TLabel.labelRelief", tk.FLAT)
    root.option_add("*TButton.labelRelief", tk.FLAT)

    # Configure ttk style for dark theme using 'clam' which is always available
    style = ttk.Style()
    style.theme_use("clam")

    # Base widget styles
    style.configure(".", bg=DARK_THEME["frame_bg"], fg=DARK_THEME["fg"],
                    fieldbackground=DARK_THEME["panel_bg"])
    style.configure("TFrame", background=DARK_THEME["frame_bg"])
    style.configure("TLabel", background=DARK_THEME["frame_bg"], foreground=DARK_THEME["fg"])
    style.configure("TLabelframe.Label", foreground=DARK_THEME["fg"])

    # TButton - visible with clear borders
    style.configure("TButton", bg=DARK_THEME["button_bg"], fg=DARK_THEME["button_fg"],
                    padding=(10, 4), borderwidth=1, relief="raised")
    style.map("TButton",
              background=[("active", DARK_THEME["button_hover_bg"]),
                         ("pressed", DARK_THEME["button_active_bg"]),
                         ("disabled", DARK_THEME["frame_bg"])],
              foreground=[("active", "#FFFFFF"),
                         ("pressed", "#FFFFFF"),
                         ("disabled", DARK_THEME["disabled_fg"])])

    # TCheckbutton
    style.configure("TCheckbutton", background=DARK_THEME["frame_bg"], foreground=DARK_THEME["fg"])
    style.map("TCheckbutton", background=[("disabled", DARK_THEME["panel_bg"])])

    # TRadiobutton
    style.configure("TRadiobutton", background=DARK_THEME["frame_bg"], foreground=DARK_THEME["fg"])
    style.map("TRadiobutton", background=[("disabled", DARK_THEME["panel_bg"])])

    # TScale
    style.configure("TScale", background=DARK_THEME["frame_bg"])
    style.configure("Horizontal.TScale.Trough", background=DARK_THEME["selected_bg"])
    style.configure("Horizontal.TScale.Thumb", background=DARK_THEME["highlight_bg"])

    # TEntry (text input) - subtle borders
    style.configure("TEntry", fieldbackground=DARK_THEME["panel_bg"], foreground=DARK_THEME["fg"],
                    borderwidth=0, relief=tk.FLAT)
    style.map("TEntry", fieldbackground=[("disabled", DARK_THEME["panel_bg"])])

    # TMenubutton
    style.configure("TMenubutton", background=DARK_THEME["button_bg"], foreground=DARK_THEME["fg"])

    # TProgressbar
    style.configure("TProgressbar", background=DARK_THEME["highlight_bg"],
                    troughcolor=DARK_THEME["selected_bg"])

    # Treeview - minimal borders for depth
    style.layout("Treeview", [("Treeview.treearea", {"sticky": "nswe"})])
    style.configure("Treeview", background=DARK_THEME["panel_bg"],
                    foreground=DARK_THEME["fg"], fieldbackground=DARK_THEME["panel_bg"],
                    bordercolor=DARK_THEME["panel_bg"], lightcolor=DARK_THEME["panel_bg"],
                    darkcolor=DARK_THEME["panel_bg"], borderwidth=0, relief=tk.FLAT)
    style.configure("Treeview.Heading", background=DARK_THEME["tree_header_bg"],
                    foreground=DARK_THEME["fg"], font=("Segoe UI", 9, "bold"),
                    bordercolor=DARK_THEME["tree_header_bg"], lightcolor=DARK_THEME["tree_header_bg"],
                    darkcolor=DARK_THEME["tree_header_bg"], borderwidth=0, relief=tk.FLAT)
    style.map("Treeview", background=[("selected", DARK_THEME["highlight_bg"]),
                                       ("alternate", DARK_THEME["selected_bg"])])

    # TScrollbar - minimal, subtle
    style.configure("TScrollbar", background=BORDER_COLORS["divider"],
                    troughcolor=DARK_THEME["border_bg"], arrowcolors=(BORDER_COLORS["inner_dark"],
                    BORDER_COLORS["inner_dark"]), bordercolor=DARK_THEME["border_bg"],
                    lightcolor=DARK_THEME["border_bg"], darkcolor=DARK_THEME["border_bg"],
                    borderwidth=0, relief=tk.FLAT)
    style.configure("Vertical.TScrollbar", gripcount=0, arrowsize=12)
    style.map("TScrollbar",
              background=[("active", DARK_THEME["button_hover_bg"]),
                          ("pressed", DARK_THEME["button_active_bg"])],
              troughcolor=[("active", DARK_THEME["border_bg"])],
              bordercolor=[("active", DARK_THEME["border_bg"]),
                           ("pressed", DARK_THEME["border_bg"])])

    # Configure menu colors via option add - minimal borders
    root.option_add("*Menu.background", BORDER_COLORS["divider"])
    root.option_add("*Menu.foreground", DARK_THEME["menu_fg"])
    root.option_add("*Menu.selectbackground", DARK_THEME["highlight_bg"])
    root.option_add("*Menu.selectforeground", "#FFFFFF")

    # Additional border and depth settings for all widgets
    root.option_add("*Frame.borderwidth", 0)
    root.option_add("*Frame.relief", tk.FLAT)
    root.option_add("*Frame.highlightThickness", 0)
    root.option_add("*Label.borderwidth", 0)
    root.option_add("*Label.relief", tk.FLAT)
    root.option_add("*Label.highlightThickness", 0)
    root.option_add("*Button.highlightThickness", 0)
    root.option_add("*Button.borderWidth", 1)
    root.option_add("*Button.relief", tk.FLAT)
    root.option_add("*Listbox.highlightThickness", 0)
    root.option_add("*Listbox.borderWidth", 0)
    root.option_add("*PanedWindow.background", DARK_THEME["border_bg"])
    root.option_add("*PanedWindow.borderWidth", 0)
    root.option_add("*PanedWindow.highlightThickness", 0)

    # Panel dividers using bordercolor option (if available)
    try:
        root.option_add("*PanedWindow.sashrelief", tk.FLAT)
        root.option_add("*PanedWindow.sashwidth", 6)
        root.option_add("*PanedWindow.sashborderwidth", 0)
    except tk.TclError:
        pass


def _apply_windows_dark_title_bar(root: tk.Tk) -> None:
    if sys.platform != "win32":
        return

    try:
        import ctypes
    except ImportError:
        return

    root.update_idletasks()
    hwnd = ctypes.windll.user32.GetParent(root.winfo_id())

    if hwnd == 0:
        hwnd = root.winfo_id()

    dark_value = ctypes.c_int(1)
    attributes = (20, 19)

    for attribute in attributes:
        ctypes.windll.dwmapi.DwmSetWindowAttribute(
            hwnd,
            attribute,
            ctypes.byref(dark_value),
            ctypes.sizeof(dark_value),
        )

    _set_dwm_color(hwnd, 34, DARK_THEME["border_bg"], ctypes)
    _set_dwm_color(hwnd, 35, DARK_THEME["frame_bg"], ctypes)
    _set_dwm_color(hwnd, 36, DARK_THEME["fg"], ctypes)


def _set_dwm_color(hwnd: int, attribute: int, hex_color: str, ctypes_module: object) -> None:
    color = _hex_to_colorref(hex_color)
    color_value = ctypes_module.c_int(color)

    ctypes_module.windll.dwmapi.DwmSetWindowAttribute(
        hwnd,
        attribute,
        ctypes_module.byref(color_value),
        ctypes_module.sizeof(color_value),
    )


def _hex_to_colorref(hex_color: str) -> int:
    value = hex_color.lstrip("#")
    red = int(value[0:2], 16)
    green = int(value[2:4], 16)
    blue = int(value[4:6], 16)

    return red | (green << 8) | (blue << 16)


def create_status_bar(root: tk.Tk, status_text_var: tk.StringVar) -> None:
    """Create and configure the status bar at the bottom of the window with dark theme."""
    status_bar = tk.Label(
        root, textvariable=status_text_var, relief=tk.FLAT, anchor=tk.W,
        font=("Segoe UI", 8), bg=DARK_THEME["border_bg"], fg=DARK_THEME["disabled_fg"],
        bd=0, highlightbackground=BORDER_COLORS["divider"], highlightthickness=0
    )
    status_bar.pack(side=tk.BOTTOM, fill=tk.X)


def create_paned_layout(root: tk.Tk) -> tuple[tk.PanedWindow, tk.PanedWindow, tk.PanedWindow]:
    """Create the main horizontal paned window with left and right sections."""
    # Main horizontal paned window
    paned = _create_dark_paned_window(root, tk.HORIZONTAL, 8)
    paned.pack(fill=tk.BOTH, expand=True)

    # Left pane (document list + tree view)
    left_paned = _create_dark_paned_window(paned, tk.VERTICAL, 6)
    paned.add(left_paned, minsize=300)

    # Right pane (preview + details)
    right_paned = _create_dark_paned_window(paned, tk.VERTICAL, 6)
    paned.add(right_paned, minsize=400)

    return paned, left_paned, right_paned


def _create_dark_paned_window(parent: tk.Widget, orient: str, sashwidth: int) -> tk.PanedWindow:
    return tk.PanedWindow(
        parent,
        orient=orient,
        bg=DARK_THEME["border_bg"],
        bd=0,
        borderwidth=0,
        relief=tk.FLAT,
        sashwidth=sashwidth,
        sashrelief=tk.FLAT,
        sashpad=0,
    )


def create_document_list_frame(left_paned: tk.PanedWindow) -> tuple[tk.Frame, tk.Listbox]:
    """Create the document list frame with a scrollable listbox (dark theme)."""
    # Frame with minimal border for depth
    doc_frame = tk.Frame(
        left_paned, bg=DARK_THEME["frame_bg"],
        highlightbackground=BORDER_COLORS["divider"], highlightthickness=0
    )
    left_paned.add(doc_frame, minsize=120)

    # Listbox with subtle border and minimal depth cues
    doc_listbox = tk.Listbox(
        doc_frame, selectmode=tk.EXTENDED, height=15, font=("Segoe UI", 9),
        bg=DARK_THEME["panel_bg"], fg=DARK_THEME["fg"],
        selectbackground=DARK_THEME["highlight_bg"], selectforeground="#FFFFFF",
        activestyle="none", borderwidth=0, relief=tk.FLAT
    )
    doc_listbox.pack(fill=tk.BOTH, expand=True)

    return doc_frame, doc_listbox


def create_tree_view_frame(
    left_paned: tk.PanedWindow, on_category_double_click: callable | None = None
) -> tuple[tk.Frame, ttk.Treeview, tk.Scrollbar]:
    """Create the tree view frame with category/label tree and scrollbar (dark theme).

    Args:
        left_paned: Parent paned window
        on_category_double_click: Optional callback when a category is double-clicked to expand it
    """
    # Frame with minimal border for depth
    tree_frame = tk.Frame(
        left_paned, bg=DARK_THEME["frame_bg"],
        highlightbackground=BORDER_COLORS["divider"], highlightthickness=0
    )
    left_paned.add(tree_frame, minsize=200)

    # Treeview with columns for sprite reference - minimal borders
    category_tree = ttk.Treeview(
        tree_frame,
        show="tree headings",
        selectmode="extended",
        yscrollcommand=lambda: None,
    )
    category_tree["columns"] = ("ref",)
    category_tree.heading("#0", text="Category / Label", anchor=tk.W)
    category_tree.heading("ref", text="Ref", anchor=tk.W)
    category_tree.column("#0", width=400, minwidth=250)
    category_tree.column("ref", width=300, minwidth=150, anchor=tk.W)
    category_tree.tag_configure("doc_header", font=("Segoe UI", 9, "bold"), foreground=DARK_THEME["selected_fg"])
    category_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

    # Scrollbar for tree view (ttk style applied via theme) - minimal appearance
    tree_scrollbar = ttk.Scrollbar(
        tree_frame, orient="vertical", command=category_tree.yview
    )
    tree_scrollbar.pack(side=tk.RIGHT, fill=tk.Y)

    # Connect scrollbar to treeview after both are created
    category_tree.configure(yscrollcommand=tree_scrollbar.set)
    tree_scrollbar.config(command=category_tree.yview)

    # Bind double-click on categories to expand them if callback provided
    if on_category_double_click is not None:
        category_tree.bind("<Double-1>", on_category_double_click)

    return tree_frame, category_tree, tree_scrollbar


def create_preview_frame(right_paned: tk.PanedWindow) -> tuple[tk.Frame, tk.Label]:
    """Create the preview frame with a centered sprite preview label (dark theme)."""
    preview_frame = tk.Frame(right_paned, bg=DARK_THEME["panel_bg"])
    right_paned.add(preview_frame, minsize=300)

    preview_label = tk.Label(
        preview_frame, text="No sprite selected", fg=DARK_THEME["disabled_fg"],
        font=("Segoe UI", 10), justify=tk.CENTER, bg=DARK_THEME["panel_bg"]
    )
    preview_label.pack(fill=tk.BOTH, expand=True)

    return preview_frame, preview_label


def create_details_frame(right_paned: tk.PanedWindow) -> tuple[tk.Frame, list[tk.Label], tk.Frame]:
    """Create the details frame with info labels and button container (dark theme)."""
    details_frame = tk.Frame(right_paned, bg=DARK_THEME["frame_bg"])
    right_paned.add(details_frame, minsize=120)

    # Info labels with dark theme colors
    doc_info_label = tk.Label(
        details_frame, text="No document selected", fg=DARK_THEME["disabled_fg"],
        font=("Segoe UI", 9), justify=tk.LEFT, bg=DARK_THEME["frame_bg"]
    )
    doc_info_label.pack(fill=tk.X, padx=5, pady=(0, 2))

    cat_info_label = tk.Label(
        details_frame, text="No category selected", fg=DARK_THEME["disabled_fg"],
        font=("Segoe UI", 9), justify=tk.LEFT, bg=DARK_THEME["frame_bg"]
    )
    cat_info_label.pack(fill=tk.X, padx=5, pady=(0, 2))

    lbl_info_label = tk.Label(
        details_frame, text="No label selected", fg=DARK_THEME["disabled_fg"],
        font=("Segoe UI", 9), justify=tk.LEFT, bg=DARK_THEME["frame_bg"]
    )
    lbl_info_label.pack(fill=tk.X, padx=5, pady=(0, 2))

    sprite_ref_label = tk.Label(
        details_frame, text="", fg=DARK_THEME["fg"], font=("Consolas", 9),
        justify=tk.LEFT, bg=DARK_THEME["frame_bg"]
    )
    sprite_ref_label.pack(fill=tk.X, padx=5, pady=(0, 2))

    # Buttons frame
    btn_frame = tk.Frame(details_frame, bg=DARK_THEME["frame_bg"])
    btn_frame.pack(fill=tk.X, padx=5, pady=5)

    return details_frame, [doc_info_label, cat_info_label, lbl_info_label, sprite_ref_label], btn_frame


def create_action_buttons(
    btn_frame: tk.Frame,
    on_open: callable,
    on_save_all: callable,
    on_scan_previews: callable,
    on_unload_all: callable,
    on_delete_without_prefix_all: callable,
) -> None:
    """Create the action button row with dark theme buttons.

    Args:
        btn_frame: Parent frame for the buttons
        on_open: Callback for opening/loading sprite library files
        on_save_all: Callback for saving all documents
        on_scan_previews: Callback for scanning previews
        on_unload_all: Callback for unloading all libraries
        on_delete_without_prefix_all: Callback for deleting labels without prefix
    """
    # Open/Load button - primary action, slightly more prominent
    open_btn = tk.Button(
        btn_frame, text="Open Library", command=on_open, width=14,
        font=("Segoe UI", 9, "bold"), bd=1, bg=DARK_THEME["button_active_bg"], fg="#FFFFFF",
        activebackground=DARK_THEME["button_hover_bg"], activeforeground="#FFFFFF",
        highlightthickness=0, relief=tk.FLAT, cursor="hand2"
    )
    open_btn.pack(side=tk.LEFT, padx=(0, 2))

    save_all_btn = tk.Button(
        btn_frame, text="Save All", command=on_save_all, width=12,
        font=("Segoe UI", 9), bd=1, bg=DARK_THEME["button_bg"], fg=DARK_THEME["button_fg"],
        activebackground=DARK_THEME["button_hover_bg"], activeforeground="#FFFFFF",
        highlightthickness=0, relief=tk.FLAT, cursor="hand2"
    )
    save_all_btn.pack(side=tk.LEFT, padx=2)

    scan_btn = tk.Button(
        btn_frame, text="Scan Previews", command=on_scan_previews, width=14,
        font=("Segoe UI", 9), bd=1, bg=DARK_THEME["button_bg"], fg=DARK_THEME["button_fg"],
        activebackground=DARK_THEME["button_hover_bg"], activeforeground="#FFFFFF",
        highlightthickness=0, relief=tk.FLAT, cursor="hand2"
    )
    scan_btn.pack(side=tk.LEFT, padx=2)

    unload_all_btn = tk.Button(
        btn_frame, text="Unload All", command=on_unload_all, width=12,
        font=("Segoe UI", 9), bd=1, bg=DARK_THEME["button_bg"], fg=DARK_THEME["button_fg"],
        activebackground=DARK_THEME["button_hover_bg"], activeforeground="#FFFFFF",
        highlightthickness=0, relief=tk.FLAT, cursor="hand2"
    )
    unload_all_btn.pack(side=tk.LEFT, padx=2)

    delete_without_prefix_btn = tk.Button(
        btn_frame, text="Delete Labels Without Prefix", command=on_delete_without_prefix_all, width=28,
        font=("Segoe UI", 9), bd=1, bg=DARK_THEME["button_bg"], fg=DARK_THEME["button_fg"],
        activebackground=DARK_THEME["button_hover_bg"], activeforeground="#FFFFFF",
        highlightthickness=0, relief=tk.FLAT, cursor="hand2"
    )
    delete_without_prefix_btn.pack(side=tk.LEFT, padx=2)


def create_context_menu(root: tk.Tk) -> tk.Menu:
    """Create a context menu for right-click operations."""
    return tk.Menu(root, tearoff=0)
