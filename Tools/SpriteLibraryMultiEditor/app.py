# Core application logic for Sprite Library Multi Editor
"""Core business logic and application orchestration for the Sprite Library Multi-Editor.

This module contains the main application class that orchestrates GUI operations
using specialized modules: ui_builder, event_handlers, document_manager, tree_operations,
preview_manager, data_operations, drag_drop, dialogs, and document_operations.
"""

import sys
import tkinter as tk  # noqa: F401
from typing import Any

# Import shared definitions from data module
from data import (  # noqa: F401
    SpriteLabel,
    SpriteCategory,
    SpriteLibraryDocument,
    CUSTOM_LIBRARY_EXTENSION,
    LEGACY_LIBRARY_EXTENSION,
    resolve_sprite_slice_name,
)

class SpriteLibraryMultiEditorApp:
    """Main application class for the Sprite Library Multi-Editor GUI."""

    def __init__(self, initial_paths: list[str] | None = None):
        self.initial_paths = initial_paths or []

        self.root = tk.Tk()
        self.root.title("Sprite Sheet Library Editor")
        self.root.minsize(1024, 768)
        self.ui_scale = 1.0

        # Apply dark theme (imported from ui_builder to avoid circular imports at module level)
        from ui_builder import _apply_dark_theme  # noqa: PLC0415
        _apply_dark_theme(self.root)

        # State
        self.documents: list[SpriteLibraryDocument] = []
        self.selected_document_index: int | None = None
        self.selected_category_index: int | None = None
        self.selected_label_index: int | None = None
        self._drag_source_iid: str | None = None
        self._drag_selection_iids: tuple[str, ...] = ()
        self._drag_start_x: int | None = None
        self._drag_start_y: int | None = None
        self._drag_min_distance = 4
        self.last_label_prefix: str = ""
        self.undo_stack: list[list[SpriteLibraryDocument]] = []
        self.redo_stack: list[list[SpriteLibraryDocument]] = []

        # Build UI
        self._build_ui()

        # Load initial paths if provided
        for path in self.initial_paths:
            self.load_path(path)

    def _build_ui(self):
        """Build the main GUI interface using ui_builder module."""
        import tkinter as tk  # noqa: PLC0415

        from ui_builder import (
            create_status_bar,
            create_paned_layout,
            create_document_list_frame,
            create_tree_view_frame,
            create_preview_frame,
            create_details_frame,
            create_action_buttons,
        )
        from event_handlers import (
            bind_document_list_select,
            bind_tree_view_events,
        )

        root = self.root
        create_status_bar(root, self.status_text)

        # Create paned layout
        _, left_paned, right_paned = create_paned_layout(root)

        # Create document list frame
        doc_frame, doc_listbox = create_document_list_frame(left_paned)

        # Create tree view frame
        tree_frame, category_tree, _ = create_tree_view_frame(left_paned)

        # Create preview frame
        preview_frame, preview_label = create_preview_frame(right_paned)

        # Create details frame
        details_frame, info_labels, btn_frame = create_details_frame(right_paned)

        # Bind events
        bind_document_list_select(doc_listbox, self._on_doc_select)
        doc_listbox.bind("<ButtonRelease-3>", self._on_doc_listbox_context_menu)
        doc_listbox.bind("<Delete>", self._on_doc_listbox_delete)
        bind_tree_view_events(
            category_tree,
            self._on_tree_select,
            self._on_category_double_click,
            self._on_context_menu,
        )
        category_tree.bind("<ButtonPress-1>", self._on_tree_button_press, add="+")
        category_tree.bind("<ButtonRelease-1>", self._on_tree_drag_drop, add="+")
        category_tree.bind("<F2>", self._on_rename_shortcut)
        category_tree.bind("<Delete>", self._on_delete_shortcut)
        category_tree.bind("<Alt-Up>", self._on_tree_move_up_shortcut)
        category_tree.bind("<Alt-Down>", self._on_tree_move_down_shortcut)
        doc_listbox.bind("<Alt-Up>", self._on_doc_move_up_shortcut)
        doc_listbox.bind("<Alt-Down>", self._on_doc_move_down_shortcut)

        # Bind undo/redo keys
        root.bind("<Control-z>", self._on_undo)
        root.bind("<Control-y>", self._on_redo)
        root.bind("<Control-Shift-Z>", self._on_redo)
        root.bind("<Control-Shift-z>", self._on_redo)
        root.bind("<Control-plus>", self._on_increase_ui_scale)
        root.bind("<Control-equal>", self._on_increase_ui_scale)
        root.bind("<Control-KP_Add>", self._on_increase_ui_scale)
        root.bind("<Control-minus>", self._on_decrease_ui_scale)
        root.bind("<Control-KP_Subtract>", self._on_decrease_ui_scale)

        # Create action buttons.
        create_action_buttons(
            btn_frame,
            self.open_library,
            self.save_all,
            self.scan_previews,
            self.unload_all,
            self.delete_labels_without_prefix_in_all_libraries,
        )

        # Store references for later use
        self.doc_listbox = doc_listbox
        self.category_tree = category_tree
        self.preview_label = preview_label
        self.info_labels = info_labels

    def _on_increase_ui_scale(self, event: Any = None) -> str:
        return self._change_ui_scale(1)

    def _on_decrease_ui_scale(self, event: Any = None) -> str:
        return self._change_ui_scale(-1)

    def _change_ui_scale(self, direction: int) -> str:
        from ui_builder import UI_SCALE_STEP, apply_ui_scale

        next_scale = self.ui_scale + (direction * UI_SCALE_STEP)
        self.ui_scale = apply_ui_scale(self.root, next_scale)
        percent = round(self.ui_scale * 100)
        self.status_text.set(f"UI scale: {percent}%")

        return "break"

    def _on_doc_select(self, event: Any) -> None:
        """Handle document list selection."""
        selection = self.doc_listbox.curselection()
        if not selection:
            return
        index = selection[0]
        self.selected_document_index = index
        doc = self.documents[index]
        self._update_doc_info(doc)

    def _on_tree_select(self, event: Any) -> None:
        """Handle tree view selection."""
        from tree_operations import parse_iid_ref

        selection = self.category_tree.selection()
        if not selection:
            return

        ref = parse_iid_ref(selection[0])
        if not self._is_valid_ref(ref):
            return

        self.selected_document_index = ref.doc_index
        self.selected_category_index = ref.cat_index
        self.selected_label_index = ref.label_index

        if ref.kind == "label" and ref.cat_index is not None and ref.label_index is not None:
            label = self.documents[ref.doc_index].categories[ref.cat_index].entries[ref.label_index]
            self._update_preview(label.sprite_ref, ref.doc_index)
            self._update_label_info(ref.doc_index, ref.cat_index, ref.label_index)
        elif ref.kind == "category" and ref.cat_index is not None:
            self._clear_preview()
            self._update_category_info(ref.doc_index, ref.cat_index)
        elif ref.kind == "document":
            self._clear_preview()
            self._update_doc_info(self.documents[ref.doc_index])
            if hasattr(self, 'doc_listbox'):
                self.doc_listbox.selection_clear(0, tk.END)
                self.doc_listbox.selection_set(ref.doc_index)
                self.doc_listbox.see(ref.doc_index)

    def _on_category_double_click(self, event: Any) -> None:
        """Handle category double-click - expands the selected category."""
        from tree_operations import parse_iid_ref

        selection = self.category_tree.selection()
        if not selection:
            return
        ref = parse_iid_ref(selection[0])
        if ref.kind == "category":
            self.category_tree.item(selection[0], open=not self.category_tree.item(selection[0], "open"))

    def _is_valid_ref(self, ref: Any) -> bool:
        if ref.kind not in {"document", "category", "label"}:
            return False
        if ref.doc_index < 0 or ref.doc_index >= len(self.documents):
            return False
        if ref.kind == "category":
            return ref.cat_index is not None and ref.cat_index < len(self.documents[ref.doc_index].categories)
        if ref.kind == "label":
            return (
                ref.cat_index is not None
                and ref.cat_index < len(self.documents[ref.doc_index].categories)
                and ref.label_index is not None
                and ref.label_index < len(self.documents[ref.doc_index].categories[ref.cat_index].entries)
            )
        return True

    def _update_doc_info(self, doc: SpriteLibraryDocument) -> None:
        """Update the document info display."""
        from event_handlers import update_doc_info
        update_doc_info(doc, self.info_labels[0])

    def _update_category_info(self, doc_index: int, cat_index: int) -> None:
        """Update the category info display."""
        from event_handlers import update_category_info
        update_category_info(
            doc_index, cat_index, self.info_labels[1], self.documents
        )

    def _update_label_info(self, doc_index: int, cat_index: int, label_index: int) -> None:
        """Update the label info display."""
        document = self.documents[doc_index]
        label = self.documents[doc_index].categories[cat_index].entries[label_index]
        slice_name = resolve_sprite_slice_name(label.sprite_ref, document.path)
        slice_text = slice_name or "(unresolved)"
        self.info_labels[2].config(
            text=(
                f"Label: {label.name}\n"
                f"Hash: {label.hash_text or '0'}\n"
                f"Slice: {slice_text}\n"
                f"Ref: {label.sprite_ref}"
            )
        )

    def _update_preview(self, sprite_ref: str | None, doc_index: int = 0) -> None:
        """Update the sprite preview display."""
        from preview_manager import update_preview_display
        update_preview_display(sprite_ref, self.preview_label, self.documents, doc_index)

    def _clear_preview(self) -> None:
        """Clear the sprite preview display."""
        self.preview_label.image = None
        self.preview_label.config(image="")
        self.preview_label.config(text="No sprite selected")

    def open_library(self) -> None:
        """Open a file dialog to select and load sheet library files."""
        import tkinter.filedialog as filedialog  # noqa: PLC0415
        from document_manager import load_path as _load_path

        file_paths = filedialog.askopenfilenames(
            title="Open Sprite Sheet Library Files",
            filetypes=[
                ("Sprite sheet libraries", (f"*{CUSTOM_LIBRARY_EXTENSION}", f"*{LEGACY_LIBRARY_EXTENSION}")),
                ("Custom sheet libraries", f"*{CUSTOM_LIBRARY_EXTENSION}"),
                ("Legacy sprite libraries", f"*{LEGACY_LIBRARY_EXTENSION}"),
                ("All files", "*.*"),
            ],
            parent=self.root,
        )
        if file_paths:
            self.undo_stack.clear()
            self.redo_stack.clear()
            for path in file_paths:
                _load_path(path, self.documents, self._refresh_doc_list, self._refresh_doc_list)

    def load_path(self, path: str) -> None:
        """Load a sheet library file from the given path."""
        from document_manager import load_path as _load_path

        self.undo_stack.clear()
        self.redo_stack.clear()
        _load_path(path, self.documents, self._refresh_doc_list, self._refresh_doc_list)

    def _refresh_doc_list(self, doc: SpriteLibraryDocument | None = None, override_selected_iid: str | None = None) -> None:
        """Refresh the document list and tree view.

        Args:
            doc: Optional document argument passed by callback (ignored)
            override_selected_iid: Optional IID to select instead of preserving current selection
        """
        from tree_operations import build_tree, parse_iid_ref
        from document_manager import refresh_document_list

        # Save selection
        if override_selected_iid:
            selected_iid = override_selected_iid
        else:
            sel = self.category_tree.selection()
            selected_iid = sel[0] if sel else None

        # Refresh the document list
        if hasattr(self, 'doc_listbox'):
            refresh_document_list(self.doc_listbox, self.documents)
            if self.selected_document_index is not None and 0 <= self.selected_document_index < len(self.documents):
                self.doc_listbox.selection_clear(0, tk.END)
                self.doc_listbox.selection_set(self.selected_document_index)
                self.doc_listbox.see(self.selected_document_index)

        # Refresh the tree view
        build_tree(self.category_tree, self.documents)

        # Restore selection if it exists in the new tree
        if selected_iid and self.category_tree.exists(selected_iid):
            self.category_tree.selection_set(selected_iid)
            self.category_tree.see(selected_iid)

            ref = parse_iid_ref(selected_iid)
            if self._is_valid_ref(ref):
                self.selected_document_index = ref.doc_index
                self.selected_category_index = ref.cat_index
                self.selected_label_index = ref.label_index

    def save_document(self, document: SpriteLibraryDocument) -> bool:
        """Save a single document."""
        from document_manager import save_document as _save_document
        return _save_document(document, self.status_text)

    def save_all(self) -> None:
        """Save all dirty documents."""
        from document_manager import save_all_documents

        saved, failed = save_all_documents(
            self.documents, self.status_text, self._refresh_doc_list
        )
        print(f"[SpriteLibEditor] Saved {saved}, Failed {failed}", file=sys.stderr)

    def scan_previews(self) -> None:
        """Scan and load all sprite previews from open documents."""
        from preview_manager import scan_all_previews as _scan_all_previews

        loaded, success = _scan_all_previews(self.documents, self.status_text)
        print(f"[SpriteLibEditor] Scanned {loaded} previews", file=sys.stderr)

    @property
    def status_text(self) -> tk.StringVar:
        """Get or create the status text variable."""
        if not hasattr(self, "_status_var"):
            self._status_var = tk.StringVar(value="Ready")
        return self._status_var

    def mainloop(self):
        """Start the Tkinter event loop."""
        self.root.mainloop()


from app_delegates import install_app_delegates  # noqa: E402

install_app_delegates(SpriteLibraryMultiEditorApp)


def main(initial_paths: list[str] | None = None) -> int:
    """Main entry point for the Sprite Library Multi-Editor."""
    app = SpriteLibraryMultiEditorApp(initial_paths)
    app.mainloop()
    return 0


if __name__ == "__main__":
    exit(main(sys.argv[1:]))
