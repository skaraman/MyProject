# Display current Locations.cs content
import sys
cs_path = 'D:\\localDev\\Unity\\MyProject\\Assets\\Scripts\\Data\\Locations.cs'
try:
    with open(cs_path, 'r', encoding='utf-8') as f:
        lines = [l for l in f.readlines()]
    print(f"File: {cs_path}")
    count=0
    for i,line in enumerate(lines[:50], 1):
        if line.strip():
            sys.stdout.write(str(i)+': '+line.rstrip())
