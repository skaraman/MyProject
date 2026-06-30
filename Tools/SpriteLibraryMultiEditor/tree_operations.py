from __future__ import annotations

# Tree Operations module for Sprite Library Multi-Editor
"""Build and manipulate the category/label tree view."""

import sys
import tkinter as tk
import tkinter.ttk as ttk
from dataclasses import dataclass
from typing import Any

LABEL_COLLAPSE_PREFIX = "|  "
@dataclass(frozen=True)
class TreeItemRef:
    kind: str
    doc_index: int
    cat_index: int | None = None
    label_index: int | None = None


def category_iid(doc_index: int, cat_index: int) -> str:
    return f"doc:{doc_index}:cat:{cat_index}"


def label_iid(doc_index: int, cat_index: int, label_index: int) -> str:
    return f"{category_iid(doc_index, cat_index)}:label:{label_index}"


def label_tree_text(label_name: str) -> str:
    return f"{LABEL_COLLAPSE_PREFIX}{label_name}"


def _get_tree_open_states(category_tree: ttk.Treeview, documents: list[SpriteLibraryDocument]) -> dict:
    """Collect open states of all document and category nodes currently in the tree."""
    open_states = {}
    for doc_index, document in enumerate(documents):
        doc_iid = f"doc:{doc_index}"
        if category_tree.exists(doc_iid):
            open_states[str(document.path)] = bool(category_tree.item(doc_iid, "open"))
        for cat_index, category in enumerate(getattr(document, 'categories', [])):
            cat_iid = category_iid(doc_index, cat_index)
            if category_tree.exists(cat_iid):
                open_states[(str(document.path), category.name)] = bool(category_tree.item(cat_iid, "open"))
    print(f"[TreeOps] Saved open states: {len(open_states)} items.", file=sys.stderr)
    return open_states


def build_tree(category_tree: ttk.Treeview, documents: list[SpriteLibraryDocument]) -> None:
    """Clear existing items and rebuild the tree from documents."""
    open_states = _get_tree_open_states(category_tree, documents)

    for item in category_tree.get_children():
        try:
            category_tree.delete(item)
        except Exception as e:
            print(f"[TreeOps] Error deleting tree item: {e}", file=sys.stderr)

    # Rebuild from documents
    for doc_index, document in enumerate(documents):
        if not hasattr(document, 'categories'):
            continue

        doc_iid = f"doc:{doc_index}"
        doc_open = open_states.get(str(document.path), True)
        try:
            category_tree.insert(
                "", tk.END, iid=doc_iid, text=document.path.name,
                values=("",), open=doc_open, tags=("doc_header",)
            )
        except Exception as e:
            print(f"[TreeOps] Error inserting document {doc_index}: {e}", file=sys.stderr)
            continue

        for cat_index, category in enumerate(getattr(document, 'categories', [])):
            cat_open = open_states.get((str(document.path), category.name), True)
            try:
                values = ("",)
                cat_item = category_tree.insert(
                    doc_iid, tk.END, iid=category_iid(doc_index, cat_index), text=category.name,
                    values=values, open=cat_open
                )
            except Exception as e:
                print(f"[TreeOps] Error inserting category {doc_index}:{cat_index}: {e}", file=sys.stderr)
                continue

            # Add labels as children
            entries = getattr(category, 'entries', [])
            for lbl_index, label in enumerate(entries):
                try:
                    values = (getattr(label, 'sprite_ref') or "",)
                    category_tree.insert(
                        cat_item, tk.END, iid=label_iid(doc_index, cat_index, lbl_index), text=label_tree_text(label.name),
                        values=values
                    )
                except Exception as e:
                    print(f"[TreeOps] Error inserting label {doc_index}:{cat_index}:{lbl_index}: {e}", file=sys.stderr)
                    continue

    # Force a redraw of the tree view
    category_tree.update()


def clear_tree(category_tree: ttk.Treeview) -> None:
    """Remove all items from the tree view."""
    for item in category_tree.get_children():
        category_tree.delete(item)


def select_item(
    category_tree: ttk.Treeview, doc_index: int, cat_index: int | None = None, lbl_index: int | None = None
) -> None:
    """Select an item in the tree view."""
    if lbl_index is not None and cat_index is not None:
        iid = label_iid(doc_index, cat_index, lbl_index)
        if category_tree.exists(iid):
            category_tree.selection_set(iid)
            category_tree.see(iid)
        return

    elif cat_index is not None and doc_index is not None:
        iid = category_iid(doc_index, cat_index)
        if category_tree.exists(iid):
            category_tree.selection_set(iid)
            category_tree.see(iid)
        return

    elif doc_index is not None:
        iid = f"doc:{doc_index}"
        if category_tree.exists(iid):
            category_tree.selection_set(iid)
            category_tree.see(iid)
        return

    else:
        category_tree.selection_remove(category_tree.selection())


def get_selected_item(category_tree: ttk.Treeview) -> str | None:
    """Get the currently selected item's text."""
    selection = category_tree.selection()
    if not selection:
        return None

    item = selection[0]
    text = category_tree.item(item)["text"]
    values = category_tree.item(item)["values"]
    return (text, values) if len(values) > 1 else text


def parse_selected_item(text: str) -> tuple[str, int | None, int | None]:
    """Compatibility wrapper for old callers."""
    ref = parse_iid_ref(text)
    index = ref.label_index if ref.kind == "label" else ref.cat_index
    return (ref.kind, ref.doc_index if ref.kind != "unknown" else None, index)


def get_item_iid(
    category_tree: ttk.Treeview, doc_index: int, cat_index: int | None = None, lbl_index: int | None = None
) -> str | None:
    """Generate an item identifier (IID) for a selected item."""
    if cat_index is None:
        return None
    if lbl_index is not None:
        return label_iid(doc_index, cat_index, lbl_index)
    return category_iid(doc_index, cat_index)


def parse_iid_ref(iid: str) -> TreeItemRef:
    """Parse a tree IID into structured indexes."""
    parts = iid.split(":")
    try:
        if len(parts) == 2 and parts[0] == "doc":
            return TreeItemRef("document", int(parts[1]))
        if len(parts) == 4 and parts[0] == "doc" and parts[2] == "cat":
            return TreeItemRef("category", int(parts[1]), int(parts[3]))
        if len(parts) == 6 and parts[0] == "doc" and parts[2] == "cat" and parts[4] == "label":
            return TreeItemRef("label", int(parts[1]), int(parts[3]), int(parts[5]))
    except ValueError:
        pass
    return TreeItemRef("unknown", -1)


def is_tree_indicator_hit(category_tree: ttk.Treeview, x: int, y: int) -> bool:
    iid = category_tree.identify_row(y)
    if not iid:
        return False

    if category_tree.identify_column(x) != "#0":
        return False

    element = category_tree.identify_element(x, y)
    return "indicator" in element.lower()


def parse_iid(iid: str) -> tuple[str, list[str]]:
    """Parse an item identifier into kind and indexes."""
    ref = parse_iid_ref(iid)
    indexes = [str(ref.doc_index)]
    if ref.cat_index is not None:
        indexes.append(str(ref.cat_index))
    if ref.label_index is not None:
        indexes.append(str(ref.label_index))
    return ref.kind, indexes


def get_selected_iid(category_tree: ttk.Treeview, selected_category_index: int | None, selected_label_index: int | None) -> str | None:
    """Get the currently selected item's IID."""
    selection = category_tree.selection()
    return selection[0] if selection else None


def get_doc_from_path(kind: str, idx: int) -> int | None:
    """Get document index from a path."""
    return idx if kind in {"category", "label"} else None


# Type stubs for external references
SpriteLibraryDocument = Any
