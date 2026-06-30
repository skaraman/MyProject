# Event Handlers module for Sprite Library Multi-Editor
"""Handle all UI events, selections, and user interactions."""

import tkinter as tk
import tkinter.ttk as ttk
from typing import Any, Callable

from data import SpriteLibraryDocument


def bind_document_list_select(doc_listbox: tk.Listbox, on_doc_select: Callable) -> None:
    """Bind document list selection event to handler."""
    doc_listbox.bind("<<ListboxSelect>>", on_doc_select)


def bind_tree_view_events(
    category_tree: ttk.Treeview,
    on_tree_select: Callable,
    on_category_double_click: Callable,
    on_context_menu: Callable,
) -> None:
    """Bind all tree view events."""
    category_tree.bind("<Double-1>", on_category_double_click)
    category_tree.bind("<ButtonRelease-3>", on_context_menu)
    category_tree.bind("<<TreeviewSelect>>", on_tree_select)


def update_doc_info(doc: SpriteLibraryDocument, doc_info_label: tk.Label) -> None:
    """Update document info display with path, category count, and entry totals."""
    path = doc.path.name if doc.path else "Unknown"
    total_entries = sum(len(cat.entries) for cat in doc.categories)
    text = f"Path: {path}\nCategories: {len(doc.categories)}\nTotal Entries: {total_entries}"
    doc_info_label.config(text=text)


def update_category_info(
    doc_index: int, cat_index: int, cat_info_label: tk.Label, documents: list[SpriteLibraryDocument]
) -> None:
    """Update category info display."""
    if doc_index is None or cat_index is None:
        return

    doc = documents[doc_index]
    if not doc.categories or cat_index >= len(doc.categories):
        return

    cat = doc.categories[cat_index]
    entry_count = len(cat.entries)
    text = f"Category: {cat.name}\nHash: {cat.hash_text or '0'}\nEntries: {entry_count}"
    cat_info_label.config(text=text)
