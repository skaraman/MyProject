from __future__ import annotations

# Document Manager module for Sprite Library Multi-Editor
"""Handle loading, saving, and managing sprite library documents."""

import sys
from typing import Callable
import tkinter as tk
import tkinter.messagebox as messagebox

# Import shared definitions and functions from data module
from data import (
    SpriteLibraryDocument,
    parse_sprite_library,
    write_sprite_library,
)


def refresh_document_list(doc_listbox: tk.Listbox, documents: list[SpriteLibraryDocument]) -> None:
    """Refresh the document list with current titles."""
    doc_listbox.delete(0, tk.END)
    for i, doc in enumerate(documents):
        title = doc.title
        doc_listbox.insert(tk.END, f"{i + 1}. {title}")


def load_path(path: str, documents: list[SpriteLibraryDocument], on_doc_loaded: Callable, refresh_doc_list_callback: Callable | None = None) -> None:
    """Load a .spriteLib file from the given path."""
    from data import expand_input_paths  # noqa: PLC0415

    paths = expand_input_paths([path])
    loaded = 0
    for p in paths:
        if not p.exists():
            continue
        # Check if the document path is already open
        resolved_p = p.resolve()
        if any(doc.path.resolve() == resolved_p for doc in documents):
            messagebox.showwarning(
                "Already Open",
                f"The sprite library '{p.name}' is already open.",
                parent=None
            )
            continue
        doc = parse_sprite_library(p)
        documents.append(doc)
        loaded += 1
        on_doc_loaded(documents[-1])
    print(f"[SpriteLibEditor] Loaded path={path} expanded={len(paths)} documents={loaded}", file=sys.stderr)
    if loaded == 0 and refresh_doc_list_callback is not None:
        refresh_doc_list_callback()


def save_document(document: SpriteLibraryDocument, status_text_var: tk.StringVar) -> bool:
    """Save a single document."""
    from tkinter.messagebox import showerror  # noqa: PLC0415

    if not document.path or not document.path.exists():
        showerror("Save Error", "Invalid path for saving.", parent=None)
        return False

    try:
        write_sprite_library(document)
        document.dirty = False
        status_text_var.set(f"Saved {document.path.name}")
        print(f"[SpriteLibEditor] Saved {document.path} dirty={document.dirty}", file=sys.stderr)
        return True
    except Exception as ex:  # noqa: BLE001
        showerror("Save Error", str(ex), parent=None)
        print(f"[SpriteLibEditor] Save failed for {document.path}: {ex}", file=sys.stderr)
        return False


def save_all_documents(
    documents: list[SpriteLibraryDocument], status_text_var: tk.StringVar, on_saved: Callable
) -> tuple[int, int]:
    """Save all dirty documents and return (saved_count, failed_count)."""
    saved = 0
    failed = 0

    for document in documents:
        if not document.dirty:
            continue
        if save_document(document, status_text_var):
            saved += 1
        else:
            failed += 1

    on_saved(documents)
    return saved, failed


def scan_all_previews(
    documents: list[SpriteLibraryDocument], status_text_var: tk.StringVar
) -> tuple[int, bool]:
    """Scan and load all sprite previews from open documents by delegating to preview_manager."""
    import preview_manager
    return preview_manager.scan_all_previews(documents, status_text_var)
