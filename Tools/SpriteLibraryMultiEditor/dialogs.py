# Dialogs and Property Editing functionality for Sprite Library Multi Editor
from __future__ import annotations

import sys
import tkinter.messagebox as messagebox
from tkinter.simpledialog import askstring
from typing import Any

from data import SpriteCategory, SpriteLibraryDocument
from tree_operations import parse_iid_ref


def edit_label_name(app: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    """Open a dialog to edit the name of a label, updating any other selected labels with the suffix change."""
    document = app.documents[doc_index]
    category = document.categories[cat_index]
    current_name = category.entries[label_index].name

    new_name = askstring(
        "Edit Label Name",
        f"Current name: {current_name}",
        parent=app.root,
        initialvalue=current_name,
    )
    if new_name is None:
        return  # User cancelled

    if new_name.strip() == "":
        messagebox.showwarning(
            "Invalid Input",
            "Label name cannot be empty.",
            parent=app.root,
        )
        return

    new_name = new_name.strip()
    selected_refs = get_selected_label_refs(app)
    suffix_refs = get_suffix_rename_refs(
        app,
        selected_refs,
        doc_index,
        cat_index,
        label_index,
        current_name,
        new_name,
    )

    app._save_undo_state()
    rename_label(document, category, label_index, new_name)
    rename_labels_with_suffix(app, suffix_refs)
    document.dirty = True
    app.refresh_tree()


def rename_label(
    document: SpriteLibraryDocument,
    category: SpriteCategory,
    label_index: int,
    new_name: str,
) -> None:
    """Update a label's name and clear its raw cached lines."""
    category.entries[label_index].name = new_name
    category.entries[label_index].raw_lines = []
    document.dirty = True


def get_selected_label_refs(app: Any) -> list[tuple[int, int, int]]:
    """Retrieve index references for all selected labels in the tree view."""
    refs = []
    for iid in app.category_tree.selection():
        ref = parse_iid_ref(iid)
        if not app._is_valid_ref(ref):
            continue
        if ref.kind != "label":
            continue
        if ref.cat_index is None:
            continue
        if ref.label_index is None:
            continue
        refs.append((ref.doc_index, ref.cat_index, ref.label_index))
    return refs


def get_suffix_rename_refs(
    app: Any,
    selected_refs: list[tuple[int, int, int]],
    doc_index: int,
    cat_index: int,
    label_index: int,
    old_name: str,
    new_name: str,
) -> list[tuple[int, int, int, str, str]]:
    """Determine suffix change and return matching rename references for selected items."""
    if len(selected_refs) <= 1:
        return []

    old_suffix, new_suffix = get_suffix_change(old_name, new_name)
    if old_suffix == new_suffix:
        return []

    refs = []
    for selected_doc_index, selected_cat_index, selected_label_index in selected_refs:
        is_source = selected_doc_index == doc_index
        is_source = is_source and selected_cat_index == cat_index
        is_source = is_source and selected_label_index == label_index
        if is_source:
            continue

        label = app.documents[selected_doc_index].categories[selected_cat_index].entries[selected_label_index]
        refs.append(
            (
                selected_doc_index,
                selected_cat_index,
                selected_label_index,
                old_suffix,
                new_suffix,
            )
        )
        print(f"[SpriteLibEditor] Batch rename target '{label.name}'", file=sys.stderr)

    return refs


def get_suffix_change(old_name: str, new_name: str) -> tuple[str, str]:
    """Find the suffix part that changed between old and new names."""
    shared_length = 0
    max_length = min(len(old_name), len(new_name))

    while shared_length < max_length:
        if old_name[shared_length] != new_name[shared_length]:
            break
        shared_length += 1

    if shared_length == 0:
        return "", ""

    return old_name[shared_length:], new_name[shared_length:]


def rename_labels_with_suffix(app: Any, refs: list[tuple[int, int, int, str, str]]) -> None:
    """Rename multiple labels by replacing their old suffix with the new one."""
    for doc_index, cat_index, label_index, old_suffix, new_suffix in refs:
        document = app.documents[doc_index]
        category = document.categories[cat_index]
        label = category.entries[label_index]
        new_name = replace_label_suffix(label.name, old_suffix, new_suffix)
        rename_label(document, category, label_index, new_name)


def replace_label_suffix(name: str, old_suffix: str, new_suffix: str) -> str:
    """Helper to perform suffix replacement in a string."""
    if old_suffix == "":
        return f"{name}{new_suffix}"

    if name.endswith(old_suffix):
        base_name = name[:-len(old_suffix)]
        return f"{base_name}{new_suffix}"

    return f"{name}{new_suffix}"


def edit_sprite_ref(app: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    """Open a dialog to edit the sprite fileID/GUID reference of a label."""
    document = app.documents[doc_index]
    category = document.categories[cat_index]
    current_ref = category.entries[label_index].sprite_ref

    new_ref = askstring(
        "Edit Sprite Reference",
        f"Current reference: {current_ref}",
        parent=app.root,
        initialvalue=current_ref,
    )
    if new_ref is None:
        return  # User cancelled

    app._save_undo_state()
    category.entries[label_index].sprite_ref = new_ref.strip() if new_ref else ""
    category.entries[label_index].raw_lines = []
    document.dirty = True
    app.refresh_tree()


def edit_category_name(app: Any, doc_index: int, cat_index: int) -> None:
    """Open a dialog to rename a category."""
    document = app.documents[doc_index]
    current_name = document.categories[cat_index].name

    new_name = askstring(
        "Edit Category Name",
        f"Current name: {current_name}",
        parent=app.root,
        initialvalue=current_name,
    )
    if new_name is None:
        return  # User cancelled

    if new_name.strip() == "":
        messagebox.showwarning(
            "Invalid Input",
            "Category name cannot be empty.",
            parent=app.root,
        )
        return

    app._save_undo_state()
    document.categories[cat_index].name = new_name.strip()
    document.dirty = True
    app.refresh_tree()


def add_category(app: Any, doc_index: int) -> None:
    """Open a dialog to add a category."""
    document = app.documents[doc_index]

    new_name = askstring(
        "Add Category",
        "Category name:",
        parent=app.root,
    )
    if new_name is None:
        return

    new_name = new_name.strip()
    if new_name == "":
        messagebox.showwarning(
            "Invalid Input",
            "Category name cannot be empty.",
            parent=app.root,
        )
        return

    app._save_undo_state()
    document.categories.append(SpriteCategory(name=new_name))
    document.dirty = True
    app.refresh_tree()
    app.status_text.set(f"Added category: {new_name}")


def has_multiple_selected_categories(app: Any) -> bool:
    """Check if more than one category is currently selected in the tree view."""
    return len(get_selected_category_refs(app)) > 1


def get_selected_category_refs(app: Any) -> list[tuple[int, int]]:
    """Retrieve index references for all selected categories in the tree view."""
    refs = []
    for iid in app.category_tree.selection():
        ref = parse_iid_ref(iid)
        if not app._is_valid_ref(ref):
            continue
        if ref.kind != "category":
            continue
        if ref.cat_index is None:
            continue
        refs.append((ref.doc_index, ref.cat_index))
    return refs


def add_suffix_to_selected_categories(app: Any) -> None:
    """Open a dialog to append a common suffix to all selected categories."""
    refs = get_selected_category_refs(app)
    if len(refs) <= 1:
        return

    suffix = askstring(
        "Add Suffix",
        "Suffix:",
        parent=app.root,
    )

    if suffix is None:
        return

    if suffix == "":
        messagebox.showwarning(
            "Invalid Input",
            "Suffix cannot be empty.",
            parent=app.root,
        )
        return

    app._save_undo_state()
    for doc_index, cat_index in refs:
        document = app.documents[doc_index]
        category = document.categories[cat_index]
        category.name = f"{category.name}{suffix}"
        document.dirty = True

    app.refresh_tree()
    app.status_text.set(f"Added suffix to {len(refs)} categories")
