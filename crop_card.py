from PIL import Image, ImageDraw
import os

img_path = r'D:\localDev\Unity\Esperanza\MyProject\Assets\Sprites\Items\Card\Basic\_1basic.jpg'
out_dir = r'D:\localDev\Unity\Esperanza\MyProject\Assets\Sprites\Items\Card\Basic\Pieces'

os.makedirs(out_dir, exist_ok=True)
img = Image.open(img_path).convert("RGBA")

# Extract meters
soul_meter = img.crop((30, 260, 120, 960))
soul_meter.save(os.path.join(out_dir, "SoulMeter.png"))

boost_meter = img.crop((840, 260, 930, 960))
boost_meter.save(os.path.join(out_dir, "BoostMeter.png"))

# Extract Name Backing
name_backing = img.crop((165, 85, 795, 215))
name_backing.save(os.path.join(out_dir, "NameBacking.png"))

# Extract Image Backing
image_backing = img.crop((335, 245, 615, 525))
image_backing.save(os.path.join(out_dir, "ImageBacking.png"))

# Extract Embellishment
embellishment = img.crop((200, 890, 760, 930))
embellishment.save(os.path.join(out_dir, "Embellishment.png"))

# Create Frame1 (Outer Frame)
frame1 = img.copy()
draw = ImageDraw.Draw(frame1)
# Make the entire inside transparent, keeping only the outer borders
# Outer frame seems to go from 0 to ~35 on top, bottom, and it has inner borders for meters.
# Let's just make a rect transparent to show the idea
draw.rectangle((130, 70, 830, 1020), fill=(0,0,0,0))
frame1.save(os.path.join(out_dir, "FrameOuter.png"))

# Create Frame2 (Inner Frame)
frame2 = img.crop((140, 75, 820, 1015))
draw2 = ImageDraw.Draw(frame2)
draw2.rectangle((15, 15, frame2.width - 15, frame2.height - 15), fill=(0,0,0,0))
frame2.save(os.path.join(out_dir, "FrameInner.png"))

# Create small background swatches by cropping a 16x16 piece of the flat colors
bg1 = img.crop((150, 400, 166, 416)) # Outer background color
bg1.save(os.path.join(out_dir, "Background1.png"))

bg2 = img.crop((200, 600, 216, 616)) # Inner background color
bg2.save(os.path.join(out_dir, "Background2.png"))

print("Slices generated successfully.")
