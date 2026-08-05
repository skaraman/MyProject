from krita import Krita, InfoObject
from PyQt5.QtCore import QByteArray
from PyQt5.QtGui import QImage
from PyQt5.QtWidgets import QFileDialog
import math
import os


HIGHLIGHT_DIRECTION_DEGREES = 135.0
RIM_WIDTH_PIXELS = 4
RIM_INTENSITY = 1.0
INTERNAL_EDGE_THRESHOLD = 0.18
INTERNAL_EDGE_INTENSITY = 0.7

app = Krita.instance()

folder = QFileDialog.getExistingDirectory(None, "Select Folder")
if not folder:
    raise Exception("No folder selected")


def is_generated_companion(path):
    stem, extension = os.path.splitext(path)
    if extension.lower() != ".png" or not stem.endswith(("N", "S")):
        return False
    return os.path.isfile(stem[:-1] + ".png")


def build_specular_pixels(source_pixels, width, height):
    source = bytearray(source_pixels)
    expected_length = width * height * 4
    if len(source) != expected_length:
        raise RuntimeError(
            "Specular export requires an RGBA 8-bit source projection "
            f"({expected_length} bytes expected, got {len(source)})."
        )

    result = bytearray(expected_length)
    angle_radians = math.radians(HIGHLIGHT_DIRECTION_DEGREES)
    direction_x = math.cos(angle_radians)
    # Image Y grows downward, so invert the mathematical Y direction. At 135
    # degrees this samples toward the top-left of the sprite.
    direction_y = -math.sin(angle_radians)
    sample_offsets = []
    for distance in range(1, max(1, RIM_WIDTH_PIXELS) + 1):
        offset_x = int(round(direction_x * distance))
        offset_y = int(round(direction_y * distance))
        if not sample_offsets or sample_offsets[-1][:2] != (offset_x, offset_y):
            sample_offsets.append((offset_x, offset_y, distance))

    def alpha_at(x, y):
        if x < 0 or x >= width or y < 0 or y >= height:
            return 0.0
        return source[((y * width + x) * 4) + 3] / 255.0

    def color_difference_at(source_offset, x, y):
        if x < 0 or x >= width or y < 0 or y >= height:
            return 0.0
        sampled_offset = (y * width + x) * 4
        return max(
            abs(source[source_offset] - source[sampled_offset]),
            abs(source[source_offset + 1] - source[sampled_offset + 1]),
            abs(source[source_offset + 2] - source[sampled_offset + 2]),
        ) / 255.0

    for y in range(height):
        for x in range(width):
            pixel_offset = (y * width + x) * 4
            source_alpha = source[pixel_offset + 3] / 255.0
            silhouette_rim = 0.0
            internal_rim = 0.0
            if source_alpha > 0.0:
                for offset_x, offset_y, distance in sample_offsets:
                    sample_x = x + offset_x
                    sample_y = y + offset_y
                    sampled_alpha = alpha_at(sample_x, sample_y)
                    edge_difference = max(0.0, source_alpha - sampled_alpha)
                    inward_fade = 1.0 - ((distance - 1.0) / max(1.0, RIM_WIDTH_PIXELS))
                    silhouette_rim = max(silhouette_rim, edge_difference * inward_fade)

                    # When both samples are inside the sprite, colour discontinuities
                    # describe internal painted edges. Sampling only toward the light
                    # direction keeps the highlight on the top-left-facing side.
                    if sampled_alpha > 0.0:
                        color_difference = color_difference_at(pixel_offset, sample_x, sample_y)
                        detected_edge = max(
                            0.0,
                            (color_difference - INTERNAL_EDGE_THRESHOLD) /
                            max(0.0001, 1.0 - INTERNAL_EDGE_THRESHOLD),
                        )
                        internal_rim = max(
                            internal_rim,
                            detected_edge * inward_fade * min(source_alpha, sampled_alpha),
                        )

            combined_rim = max(
                silhouette_rim * RIM_INTENSITY,
                internal_rim * INTERNAL_EDGE_INTENSITY,
            )
            specular = int(round(min(1.0, combined_rim) * 255.0))
            result[pixel_offset] = specular
            result[pixel_offset + 1] = specular
            result[pixel_offset + 2] = specular
            result[pixel_offset + 3] = source[pixel_offset + 3]

    return QByteArray(bytes(result))


def read_merged_projection_pixels(doc, width, height):
    app.setActiveDocument(doc)
    doc.waitForDone()
    doc.refreshProjection()
    doc.waitForDone()

    expected_length = width * height * 4
    root = doc.rootNode()
    if root is not None:
        projected_pixels = root.projectionPixelData(0, 0, width, height)
        if len(projected_pixels) == expected_length:
            return bytes(projected_pixels)

    projection = doc.projection()
    if projection is None or projection.isNull():
        raise RuntimeError("Krita did not produce a merged image projection.")

    rgba_projection = projection.convertToFormat(QImage.Format_RGBA8888)
    source_bits = rgba_projection.bits()
    source_bits.setsize(rgba_projection.byteCount())
    return bytes(source_bits)


def process_png(path):
    base_path = os.path.splitext(path)[0]
    out_path = base_path + "S.png"

    if is_generated_companion(path):
        print("Skipping generated companion:", path)
        return

    if os.path.exists(out_path):
        print("Skipping, specular map exists:", out_path)
        return

    print("Processing:", path)
    doc = app.openDocument(path)
    if not doc:
        print("Failed to open:", path)
        return

    doc.setBatchmode(True)
    width = doc.width()
    height = doc.height()

    try:
        source_pixels = read_merged_projection_pixels(doc, width, height)
        specular_pixels = build_specular_pixels(source_pixels, width, height)
    except RuntimeError as ex:
        print("Skipping:", path, "-", ex)
        doc.close()
        return

    result = app.createDocument(
        width,
        height,
        os.path.basename(base_path) + "S",
        "RGBA",
        "U8",
        doc.colorProfile() or "sRGB-elle-V2-srgbtrc.icc",
        doc.resolution(),
    )
    result.setBatchmode(True)
    result_layer = result.createNode("Specular Mask", "paintlayer")
    result.rootNode().addChildNode(result_layer, None)
    result_layer.setPixelData(specular_pixels, 0, 0, width, height)
    result.refreshProjection()

    export_config = InfoObject()
    export_config.setProperty("alpha", True)
    export_config.setProperty("compression", 6)
    export_config.setProperty("forceSRGB", False)
    export_config.setProperty("indexed", False)
    export_config.setProperty("interlaced", False)
    export_config.setProperty("saveSRGBProfile", False)

    if not result.exportImage(out_path, export_config):
        print("Failed to export:", out_path)
        result.close()
        doc.close()
        return

    print("Saved top-left directional rim specular mask:", out_path)
    result.close()
    doc.close()


for root, dirs, files in os.walk(folder):
    for filename in files:
        if filename.lower().endswith(".png"):
            process_png(os.path.join(root, filename))

print("Done")
