"""Check that native placement screenshots put green edges on terrain cells.

Run medieval-placement first. This catches the double zoom that a mouse
projection round-trip alone cannot detect.
"""
import json
import sys
from pathlib import Path

from PIL import Image

root = Path(__file__).resolve().parents[3]
reports = [Path(p) for p in sys.argv[1:]] or sorted(
    (root / "analysis/medieval/placement-preview").glob("ui-*/result.json")
)
if not reports:
    raise SystemExit("Run the medieval-placement native scenario first.")

failures = []
checked = 0
for path in reports:
    report = json.loads(path.read_text(encoding="utf-8"))
    if not report["passed"]:
        failures.append(f"{path}: native checks failed: {report['errors']}")
    for capture in report["screenshots"]:
        with Image.open(path.parent / capture["filename"]) as image:
            pixels = image.convert("RGB")
            missing = []
            for x, y in capture["points"]:
                # Two pixels allow for antialiasing and fractional UI scaling.
                colors = [pixels.getpixel((px, py))
                          for py in range(max(0, y - 2), min(image.height, y + 3))
                          for px in range(max(0, x - 2), min(image.width, x + 3))]
                if not any(g > 130 and g > r * 1.7 and g > b * 1.7 for r, g, b in colors):
                    missing.append([x, y])
            if missing:
                failures.append(f"{path.parent.name}/{capture['filename']}: "
                                f"{len(missing)} misplaced edges: {missing[:6]}")
            checked += 1

if failures:
    raise SystemExit("\n".join(failures))
print(f"PASS: {checked} native screenshots; every sampled footprint edge is aligned.")
