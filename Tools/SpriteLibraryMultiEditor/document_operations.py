# Document and Item operations for Sprite Library Multi Editor
from __future__ import annotations

import sys
import tkinter as tk
import tkinter.messagebox as messagebox
from tkinter.simpledialog import askstring
from typing import Any

from data_operations import (
    remove_category_from_document,
    remove_label_from_category,
    remove_labels_without_prefix,
)
from tree_operations import parse_iid_ref


def on_delete_shortcut(app: Any, event: Any = None) -> None:
    """Handle Delete key shortcut for single or multiple selected tree items."""
    selection = app.category_tree.selection()
    if not selection:
        return

    # Parse all selected items and filter out invalid ones
    refs = [parse_iid_ref(iid) for iid in selection]
    refs = [ref for ref in refs if app._is_valid_ref(ref)]
    if not refs:
        return

    # Resolve references to the actual objects
    docs_to_unload = []
    categories_to_delete = []
    labels_to_delete = []

    for ref in refs:
        doc = app.documents[ref.doc_index]
        if ref.kind == "document":
            if doc not in docs_to_unload:
                docs_to_unload.append(doc)
        elif ref.kind == "category":
            cat = doc.categories[ref.cat_index]
            categories_to_delete.append((doc, cat))
        elif ref.kind == "label":
            cat = doc.categories[ref.cat_index]
            lbl = cat.entries[ref.label_index]
            labels_to_delete.append((doc, cat, lbl))

    # Filter out redundant deletions (e.g. child items whose parent is also being deleted)
    categories_to_delete = [
        (doc, cat) for doc, cat in categories_to_delete
        if doc not in docs_to_unload
    ]

    deleted_cat_ids = {id(cat) for _, cat in categories_to_delete}
    labels_to_delete = [
        (doc, cat, lbl) for doc, cat, lbl in labels_to_delete
        if doc not in docs_to_unload and id(cat) not in deleted_cat_ids
    ]

    if not (docs_to_unload or categories_to_delete or labels_to_delete):
        return

    # Prompt to save dirty documents that are being unloaded
    for doc in docs_to_unload:
        if doc.dirty:
            response = messagebox.askyesnocancel(
                "Save Changes",
                f"Save changes to '{doc.title or doc.path.name}' before closing?",
                parent=app.root,
            )
            if response is None:  # Cancel
                return
            elif response is True:  # Yes
                if not app.save_document(doc):
                    return

    # Save undo state once for the entire batch operation
    app._save_undo_state()

    # Unload documents
    for doc in docs_to_unload:
        if doc in app.documents:
            app.documents.remove(doc)

    # Delete categories
    for doc, cat in categories_to_delete:
        if doc in app.documents and cat in doc.categories:
            remove_category_from_document(doc, cat)
            doc.dirty = True

    # Delete labels
    for doc, cat, lbl in labels_to_delete:
        if doc in app.documents and cat in doc.categories and lbl in cat.entries:
            lbl_idx = cat.entries.index(lbl)
            remove_label_from_category(doc, cat, lbl_idx)
            doc.dirty = True

    # Reset selection states
    app.selected_document_index = None
    app.selected_category_index = None
    app.selected_label_index = None

    app._refresh_doc_list()

    # Update preview/info display
    if not app.documents:
        app.preview_label.config(image="", text="No sprite selected")
        for label in app.info_labels:
            label.config(text="")
    else:
        app.info_labels[0].config(text="No document selected")
        app.info_labels[1].config(text="No category selected")
        app.info_labels[2].config(text="No label selected")
        app.info_labels[3].config(text="")

    # Construct and set status message
    parts = []
    if docs_to_unload:
        parts.append(f"{len(docs_to_unload)} library/libraries unloaded")
    if categories_to_delete:
        parts.append(f"{len(categories_to_delete)} category/categories deleted")
    if labels_to_delete:
        parts.append(f"{len(labels_to_delete)} label(s) deleted")
    if parts:
        app.status_text.set("; ".join(parts))


def delete_label(app: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    """Delete a label from its category."""
    app._save_undo_state()
    document = app.documents[doc_index]
    category = document.categories[cat_index]
    remove_label_from_category(document, category, label_index)
    document.dirty = True
    app.refresh_tree()


def delete_category(app: Any, doc_index: int, cat_index: int) -> None:
    """Delete a category from its document."""
    app._save_undo_state()
    document = app.documents[doc_index]
    category = document.categories[cat_index]
    remove_category_from_document(document, category)
    document.dirty = True
    app.refresh_tree()


def delete_labels_without_prefix(app: Any, doc_index: int, cat_index: int) -> None:
    """Delete labels in a category that do not match a prefix."""
    document = app.documents[doc_index]
    category = document.categories[cat_index]
    prefix = askstring(
        "Delete Labels Without Prefix",
        "Keep labels starting with:",
        parent=app.root,
        initialvalue=app.last_label_prefix,
    )

    if prefix is None:
        return

    prefix = prefix.strip()
    if prefix == "":
        messagebox.showwarning(
            "Invalid Prefix",
            "Prefix cannot be empty.",
            parent=app.root,
        )
        return

    app.last_label_prefix = prefix
    app._save_undo_state()
    removed_count = remove_labels_without_prefix(
        document,
        category,
        prefix,
    )

    if removed_count == 0:
        if app.undo_stack:
            app.undo_stack.pop()
        app.status_text.set("No labels removed")
        return

    document.dirty = True
    app.refresh_tree()
    app.status_text.set(f"Deleted {removed_count} label(s) without prefix '{prefix}'")


def delete_labels_without_prefix_in_document(app: Any, doc_index: int) -> None:
    """Delete labels in all categories of a document that do not match a prefix."""
    document = app.documents[doc_index]
    prefix = askstring(
        "Delete Labels Without Prefix",
        f"Keep labels starting with in '{document.path.name}':",
        parent=app.root,
        initialvalue=app.last_label_prefix,
    )

    if prefix is None:
        return

    prefix = prefix.strip()
    if prefix == "":
        messagebox.showwarning(
            "Invalid Prefix",
            "Prefix cannot be empty.",
            parent=app.root,
        )
        return

    app.last_label_prefix = prefix
    app._save_undo_state()

    total_removed = 0
    for category in document.categories:
        removed_count = remove_labels_without_prefix(
            document,
            category,
            prefix,
        )
        total_removed += removed_count

    if total_removed == 0:
        if app.undo_stack:
            app.undo_stack.pop()
        app.status_text.set("No labels removed")
        return

    document.dirty = True
    app._refresh_doc_list()
    app.status_text.set(f"Deleted {total_removed} label(s) without prefix '{prefix}' from '{document.path.name}'")


def delete_labels_without_prefix_in_all_libraries(app: Any) -> None:
    """Delete labels in every loaded library that do not match a prefix."""
    if not app.documents:
        return

    prefix = askstring(
        "Delete Labels Without Prefix",
        "Keep labels starting with in all libraries:",
        parent=app.root,
        initialvalue=app.last_label_prefix,
    )

    if prefix is None:
        return

    prefix = prefix.strip()
    if prefix == "":
        messagebox.showwarning(
            "Invalid Prefix",
            "Prefix cannot be empty.",
            parent=app.root,
        )
        return

    app.last_label_prefix = prefix
    app._save_undo_state()

    total_removed = 0
    for document in app.documents:
        document_removed = 0
        for category in document.categories:
            removed_count = remove_labels_without_prefix(
                document,
                category,
                prefix,
            )
            document_removed += removed_count
            total_removed += removed_count
        if document_removed > 0:
            document.dirty = True

    if total_removed == 0:
        if app.undo_stack:
            app.undo_stack.pop()
        app.status_text.set("No labels removed")
        return

    app._refresh_doc_list()
    app.status_text.set(f"Deleted {total_removed} label(s) without prefix '{prefix}' from all libraries")


def unload_document(app: Any, index: int) -> bool:
    """Unload a document at the given index. Return True if unloaded, False if cancelled."""
    if index < 0 or index >= len(app.documents):
        return False

    doc = app.documents[index]
    if doc.dirty:
        response = messagebox.askyesnocancel(
            "Save Changes",
            f"Save changes to '{doc.title or doc.path.name}' before closing?",
            parent=app.root,
        )
        if response is None:  # Cancel
            return False
        elif response is True:  # Yes
            if not app.save_document(doc):
                return False

    app._save_undo_state()
    app.documents.pop(index)

    app.selected_document_index = None
    app.selected_category_index = None
    app.selected_label_index = None

    app._refresh_doc_list()

    if not app.documents:
        app.preview_label.config(image="", text="No sprite selected")
        for label in app.info_labels:
            label.config(text="")
    else:
        app.info_labels[0].config(text="No document selected")
        app.info_labels[1].config(text="No category selected")
        app.info_labels[2].config(text="No label selected")
        app.info_labels[3].config(text="")

    app.status_text.set(f"Unloaded {doc.path.name if doc.path else 'library'}")
    return True


def unload_all(app: Any) -> None:
    """Unload all documents, asking to save changes for any dirty ones."""
    if not app.documents:
        return

    # Check all dirty documents first to see if we should proceed
    for doc in list(app.documents):
        if doc.dirty:
            response = messagebox.askyesnocancel(
                "Save Changes",
                f"Save changes to '{doc.title or doc.path.name}' before closing?",
                parent=app.root,
            )
            if response is None:  # Cancel
                return
            elif response is True:  # Yes
                if not app.save_document(doc):
                    return  # If save failed, abort unloading

    # Save undo state once before clearing all documents
    app._save_undo_state()
    app.documents.clear()

    app.selected_document_index = None
    app.selected_category_index = None
    app.selected_label_index = None

    app._refresh_doc_list()

    app.preview_label.config(image="", text="No sprite selected")
    for label in app.info_labels:
        label.config(text="")

    app.status_text.set("Unloaded all libraries")


def on_doc_listbox_delete(app: Any, event: Any = None) -> None:
    """Handle Delete key on document listbox."""
    selection = app.doc_listbox.curselection()
    if not selection:
        return
    indices = sorted(list(selection), reverse=True)
    for index in indices:
        unload_document(app, index)


def move_and_suffix_into_document(app: Any, target_doc_index: int) -> None:
    """Move and suffix all categories from other documents into a target document."""
    from data import merge_category

    if len(app.documents) <= 1:
        app.status_text.set("Move and Suffix needs at least two libraries")
        return

    app._save_undo_state()
    target_doc = app.documents[target_doc_index]
    moved_count = 0

    for source_doc_index, source_doc in enumerate(app.documents):
        if source_doc_index == target_doc_index:
            continue

        suffix = get_library_category_suffix(source_doc)
        if suffix == "":
            continue

        for source_category in source_doc.categories:
            category = source_category.clone()
            category.name = f"{category.name}_{suffix}"

            if merge_category(target_doc, category, replace=True):
                moved_count += 1

    if moved_count == 0:
        if app.undo_stack:
            app.undo_stack.pop()
        app.status_text.set("No categories moved")
        return

    target_doc.dirty = True
    app._refresh_doc_list()
    app.status_text.set(f"Moved {moved_count} suffixed category(ies) into {target_doc.path.name}")


def get_library_category_suffix(document: Any) -> str:
    """Helper to generate a clean suffix from a library path stem."""
    suffix = document.path.stem
    suffix = suffix.replace("Gear", "")
    suffix = suffix.replace("GEAR", "")
    suffix = suffix.replace("gear", "")
    suffix = suffix.replace("Geaer", "")
    suffix = suffix.replace("GEAER", "")
    suffix = suffix.replace("geaer", "")
    return suffix.strip("_ -")


def move_and_rename_into_category(app: Any, target_doc_index: int, target_cat_index: int) -> None:
    """Move named labels from each other document into a category."""
    from data import find_label, merge_labels

    if len(app.documents) <= 1:
        app.status_text.set("Move and Rename needs at least two libraries")
        return

    target_doc = app.documents[target_doc_index]
    target_category = target_doc.categories[target_cat_index]
    label_name = ask_move_and_rename_label_name(app, target_category.name)

    if label_name is None:
        return

    app._save_undo_state()
    moved_count = 0

    for source_doc_index, source_doc in enumerate(app.documents):
        if source_doc_index == target_doc_index:
            continue

        moved_labels = pop_renamed_labels_from_document(
            source_doc,
            label_name,
            find_label,
        )

        if not moved_labels:
            continue

        if merge_labels(target_category, moved_labels, replace=True):
            moved_count += len(moved_labels)

        source_doc.dirty = True

    if moved_count == 0:
        if app.undo_stack:
            app.undo_stack.pop()
        app.status_text.set(f"No labels named '{label_name}' moved")
        return

    target_doc.dirty = True
    app._refresh_doc_list()
    app.status_text.set(f"Moved and renamed {moved_count} label(s) into {target_category.name}")


def ask_move_and_rename_label_name(app: Any, target_category_name: str) -> str | None:
    label_name = askstring(
        "Move and Rename",
        f"Label name to move into '{target_category_name}':",
        parent=app.root,
    )

    if label_name is None:
        return None

    label_name = label_name.strip()
    if label_name != "":
        return label_name

    messagebox.showwarning(
        "Invalid Input",
        "Label name cannot be empty.",
        parent=app.root,
    )
    return None


def pop_renamed_labels_from_document(
    document: Any,
    label_name: str,
    find_label_func: Any,
) -> list[Any]:
    moved_labels = []

    for category in document.categories:
        while True:
            label_index = find_label_func(category, label_name)

            if label_index < 0:
                break

            label = category.entries.pop(label_index)
            moved_label = label.clone()
            moved_label.name = category.name
            moved_label.raw_lines = []
            moved_labels.append(moved_label)

    return moved_labels
