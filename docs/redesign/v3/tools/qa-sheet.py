"""Склейка кадров QA-прогона в обзорные листы: серия f0..fN — в одну полосу, отдельные кадры — по одному.

python tools/qa-sheet.py <папка-с-кадрами> <папка-для-листов> [макс-ширина-кадра]
"""
import re
import sys
from collections import OrderedDict
from pathlib import Path

from PIL import Image, ImageDraw

src, dst = Path(sys.argv[1]), Path(sys.argv[2])
max_w = int(sys.argv[3]) if len(sys.argv) > 3 else 520
dst.mkdir(parents=True, exist_ok=True)

groups = OrderedDict()
for f in sorted(src.glob('*.png')):
    m = re.match(r'\d+-(.+?)(?:-f(\d+))?\.png$', f.name)
    key = m.group(1) if m.group(2) is not None else f.stem
    groups.setdefault(key, []).append(f)

for key, files in groups.items():
    imgs = [Image.open(f).convert('RGB') for f in files]
    scale = min(1.0, max_w / imgs[0].width)
    imgs = [im.resize((int(im.width * scale), int(im.height * scale))) for im in imgs]
    cols = min(len(imgs), 4)
    rows = (len(imgs) + cols - 1) // cols
    w, h = imgs[0].width, imgs[0].height
    sheet = Image.new('RGB', (cols * w + (cols - 1) * 6, rows * (h + 18)), (20, 20, 24))
    draw = ImageDraw.Draw(sheet)
    for i, im in enumerate(imgs):
        x, y = (i % cols) * (w + 6), (i // cols) * (h + 18)
        sheet.paste(im, (x, y + 18))
        draw.text((x + 4, y + 3), f'{key} #{i}', fill=(200, 200, 200))
    sheet.save(dst / f'{key}.png')
print('\n'.join(groups))
