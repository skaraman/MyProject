from krita import Krita, InfoObject
from PyQt5.QtWidgets import QFileDialog
import os

app = Krita.instance()

folder = QFileDialog.getExistingDirectory(None, "Select Folder")
if not folder:
    raise Exception("No folder selected")

# Use the correct filter name
filter = app.filter("height to normal")
if not filter:
    raise Exception("Height to Normal Map filter not registered")

def get_paint_layer(node):
    if node.type() == "paintlayer":
        return node
    for c in node.childNodes():
        r = get_paint_layer(c)
        if r:
            return r
    return None

def process_png(path):
    print("Processing:", path)
    
    doc = app.openDocument(path)
    if not doc:
        print("Failed to open:", path)
        return
    
    # Enable batch mode to suppress dialogs
    doc.setBatchmode(True)
    
    app.setActiveDocument(doc)
    
    layer = get_paint_layer(doc.rootNode())
    if not layer:
        print("No paint layer:", path)
        doc.close()
        return
    
    # Get layer bounds
    bounds = layer.bounds()
    
    # Apply filter with position and size arguments (no config)
    filter.apply(
        layer,
        bounds.x(),
        bounds.y(),
        bounds.width(),
        bounds.height()
    )
    
    doc.refreshProjection()
    
    out_path = os.path.splitext(path)[0] + ".jpg"
    
    # Save without dialog using saveAs
    doc.saveAs(out_path)
    print("Saved:", out_path)
    
    doc.close()

for root, dirs, files in os.walk(folder):
    for f in files:
        if f.lower().endswith(".png"):
            process_png(os.path.join(root, f))

print("Done")