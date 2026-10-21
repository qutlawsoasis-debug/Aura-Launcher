"""Участок мира Minecraft → меш для three.js по родным моделям блоков (blockstates + models).

python mesher.py area.json mcassets out/
На выходе: out/scene.bin (геометрия), out/scene.json (описание буферов), out/atlas.png.
"""
import json
import math
import os
import struct
import sys
from functools import lru_cache

from PIL import Image

AREA, ASSETS, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
MC = os.path.join(ASSETS, 'assets', 'minecraft')
os.makedirs(OUT, exist_ok=True)

area = json.load(open(AREA))
OX, OY, OZ = area['origin']
SX, SY, SZ = area['size']
PALETTE = area['palette']
DATA = area['data']


def idx(x, y, z):
    return (y * SZ + z) * SX + x


def state_at(x, y, z):
    if 0 <= x < SX and 0 <= y < SY and 0 <= z < SZ:
        return PALETTE[DATA[idx(x, y, z)]]
    return 'minecraft:air'


def split_state(s):
    if '[' in s:
        name, props = s[:-1].split('[', 1)
        return name, dict(p.split('=') for p in props.split(','))
    return s, {}


# ---------- Модели ----------

@lru_cache(None)
def load_json(path):
    try:
        with open(path, encoding='utf8') as f:
            return json.load(f)
    except FileNotFoundError:
        return None


def model_path(ref):
    ref = ref.split(':')[-1]
    return os.path.join(MC, 'models', ref + '.json')


@lru_cache(None)
def resolve_model(ref):
    """Модель с учётом родителей: итоговые textures и elements."""
    textures, elements, chain = {}, None, ref
    seen = 0
    while chain and seen < 20:
        m = load_json(model_path(chain))
        if m is None:
            break
        for k, v in m.get('textures', {}).items():
            textures.setdefault(k, v)
        if elements is None and 'elements' in m:
            elements = m['elements']
        chain = m.get('parent')
        seen += 1

    def tex(name):
        v, hops = name, 0
        while v.startswith('#') and hops < 10:
            v = textures.get(v[1:], '')
            hops += 1
        return v.split(':')[-1] if v else None

    return elements or [], tex


def matches(when, props):
    if 'OR' in when:
        return any(matches(w, props) for w in when['OR'])
    if 'AND' in when:
        return all(matches(w, props) for w in when['AND'])
    return all(props.get(k) in str(v).split('|') for k, v in when.items())


@lru_cache(None)
def block_models(state):
    """Список (model_ref, x_rot, y_rot, uvlock) для состояния блока."""
    name, props = split_state(state)
    bs = load_json(os.path.join(MC, 'blockstates', name.split(':')[-1] + '.json')) if name.startswith('minecraft:') else None
    if not bs:
        return []
    out = []
    if 'variants' in bs:
        for key, val in bs['variants'].items():
            need = dict(p.split('=') for p in key.split(',')) if key else {}
            if all(props.get(k) == v for k, v in need.items()):
                v = val[0] if isinstance(val, list) else val
                out.append((v['model'], v.get('x', 0), v.get('y', 0), v.get('uvlock', False)))
                break
    for part in bs.get('multipart', []):
        if 'when' not in part or matches(part['when'], props):
            v = part['apply'][0] if isinstance(part['apply'], list) else part['apply']
            out.append((v['model'], v.get('x', 0), v.get('y', 0), v.get('uvlock', False)))
    return out


TRANSLUCENT = ('water', 'ice', 'stained_glass', 'slime', 'honey_block')
NONSOLID_HINT = ('leaves', 'glass', 'grass', 'fern', 'flower', 'sapling', 'torch', 'lantern', 'carpet',
                 'slab', 'stairs', 'fence', 'wall', 'door', 'trapdoor', 'pane', 'bars', 'rail', 'button',
                 'pressure', 'sign', 'bamboo', 'vine', 'chain', 'candle', 'bed', 'chest', 'snow', 'path',
                 'farmland', 'ladder', 'lever', 'scaffolding', 'campfire', 'azalea', 'dripleaf', 'cobweb')


@lru_cache(None)
def is_full_opaque(state):
    name = state.split('[')[0]
    if name in ('minecraft:air', 'minecraft:cave_air', 'minecraft:void_air'):
        return False
    if not name.startswith('minecraft:'):
        return False
    base = name.split(':')[1]
    if any(h in base for h in NONSOLID_HINT) or any(t in base for t in TRANSLUCENT):
        return False
    models = block_models(state)
    if len(models) != 1:
        return False
    elements, _ = resolve_model(models[0][0])
    return len(elements) == 1 and elements[0]['from'] == [0, 0, 0] and elements[0]['to'] == [16, 16, 16]


def render_layer(state):
    base = state.split('[')[0].split(':')[-1]
    if any(t in base for t in TRANSLUCENT):
        return 'translucent'
    if any(h in base for h in ('leaves', 'glass', 'grass', 'fern', 'flower', 'sapling', 'torch', 'lantern',
                               'bamboo', 'vine', 'rail', 'door', 'trapdoor', 'pane', 'bars', 'chain', 'cobweb',
                               'azalea', 'dripleaf', 'ladder', 'scaffolding', 'campfire', 'tulip', 'poppy',
                               'dandelion', 'orchid', 'allium', 'bluet', 'daisy', 'cornflower', 'lily', 'rose',
                               'bush', 'kelp', 'seagrass', 'petals', 'sugar_cane', 'wheat', 'carrots', 'potatoes')):
        return 'cutout'
    return 'opaque'


# Оттенки биома (Minecraft красит по цветовой карте; берём постоянные значения для этой сцены)
GRASS_TINT = (0x91, 0xBD, 0x59)
FOLIAGE_TINT = (0x6A, 0xA0, 0x3A)
SPRUCE_TINT = (0x61, 0x99, 0x61)
BIRCH_TINT = (0x80, 0xA7, 0x55)
WATER_TINT = (0x3F, 0x76, 0xE4)


def tint_for(state, tintindex):
    if tintindex is None or tintindex < 0:
        return (255, 255, 255)
    base = state.split('[')[0].split(':')[-1]
    if base in ('cherry_leaves', 'azalea_leaves', 'flowering_azalea_leaves', 'pink_petals'):
        return (255, 255, 255)
    if 'spruce_leaves' in base:
        return SPRUCE_TINT
    if 'birch_leaves' in base:
        return BIRCH_TINT
    if 'leaves' in base or 'vine' in base:
        return FOLIAGE_TINT
    if 'water' in base:
        return WATER_TINT
    return GRASS_TINT


# ---------- Атлас ----------

class Atlas:
    def __init__(self):
        self.slots = {}

    def slot(self, tex):
        if tex not in self.slots:
            self.slots[tex] = len(self.slots)
        return self.slots[tex]

    def build(self):
        n = len(self.slots)
        cols = math.ceil(math.sqrt(n))
        self.cols = cols
        size = cols * 16
        img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
        for tex, i in self.slots.items():
            p = os.path.join(MC, 'textures', tex + '.png')
            try:
                t = Image.open(p).convert('RGBA')
                t = t.crop((0, 0, t.width, t.width)).resize((16, 16), Image.NEAREST)
            except FileNotFoundError:
                t = Image.new('RGBA', (16, 16), (255, 0, 255, 255))
            img.paste(t, ((i % cols) * 16, (i // cols) * 16))
        self.size = size
        return img

    def uv(self, tex, u, v):
        """u, v в пикселях текстуры 0..16 → координаты атласа 0..1 (v вниз, three.js flipY=false)."""
        i = self.slots[tex]
        x0, y0 = (i % self.cols) * 16, (i // self.cols) * 16
        return ((x0 + u) / self.size, (y0 + v) / self.size)


atlas = Atlas()

# ---------- Геометрия ----------

FACE_DIR = {'down': (0, -1, 0), 'up': (0, 1, 0), 'north': (0, 0, -1), 'south': (0, 0, 1), 'west': (-1, 0, 0), 'east': (1, 0, 0)}
SHADE = {'up': 1.0, 'down': 0.5, 'north': 0.8, 'south': 0.8, 'west': 0.6, 'east': 0.6}


def face_corners(face, f, t):
    """Углы грани (в 0..16) в порядке, согласованном с UV [u0,v0]→[u1,v1] как в Minecraft."""
    (x0, y0, z0), (x1, y1, z1) = f, t
    return {
        'down':  [(x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0)],
        'up':    [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)],
        'north': [(x1, y1, z0), (x0, y1, z0), (x0, y0, z0), (x1, y0, z0)],
        'south': [(x0, y1, z1), (x1, y1, z1), (x1, y0, z1), (x0, y0, z1)],
        'west':  [(x0, y1, z0), (x0, y1, z1), (x0, y0, z1), (x0, y0, z0)],
        'east':  [(x1, y1, z1), (x1, y1, z0), (x1, y0, z0), (x1, y0, z1)],
    }[face]


def default_uv(face, f, t):
    (x0, y0, z0), (x1, y1, z1) = f, t
    return {
        'down': [x0, 16 - z1, x1, 16 - z0], 'up': [x0, z0, x1, z1],
        'north': [16 - x1, 16 - y1, 16 - x0, 16 - y0], 'south': [x0, 16 - y1, x1, 16 - y0],
        'west': [z0, 16 - y1, z1, 16 - y0], 'east': [16 - z1, 16 - y1, 16 - z0, 16 - y0],
    }[face]


def rot_point(p, axis, angle, origin, rescale=False):
    a = math.radians(angle)
    c, s = math.cos(a), math.sin(a)
    x, y, z = p[0] - origin[0], p[1] - origin[1], p[2] - origin[2]
    if axis == 'x':
        y, z = y * c - z * s, y * s + z * c
    elif axis == 'y':
        x, z = x * c + z * s, -x * s + z * c
    else:
        x, y = x * c - y * s, x * s + y * c
    if rescale and angle % 90:
        k = 1 / math.cos(math.radians(abs(angle)))
        if axis == 'x':
            y, z = y * k, z * k
        elif axis == 'y':
            x, z = x * k, z * k
        else:
            x, y = x * k, y * k
    return (x + origin[0], y + origin[1], z + origin[2])


def rot_block(p, xr, yr):
    """Поворот blockstate вокруг центра блока: сначала x, потом y (как в Minecraft)."""
    if xr:
        p = rot_point(p, 'x', -xr, (8, 8, 8))
    if yr:
        p = rot_point(p, 'y', -yr, (8, 8, 8))
    return p


def rot_dir(d, xr, yr):
    p = rot_block((8 + d[0], 8 + d[1], 8 + d[2]), xr, yr)
    v = (round(p[0] - 8), round(p[1] - 8), round(p[2] - 8))
    for k, vv in FACE_DIR.items():
        if vv == v:
            return k
    return None


layers = {k: {'pos': [], 'uv': [], 'col': []} for k in ('opaque', 'cutout', 'translucent')}


def emit(layer, corners, uvs, color, shades=None):
    L = layers[layer]
    order = (0, 1, 2, 0, 2, 3)
    # Диагональ квадрата выбираем по AO, чтобы не было «залома» тени (как в Minecraft)
    if shades and shades[0] + shades[2] < shades[1] + shades[3]:
        order = (1, 2, 3, 1, 3, 0)
    for i in order:
        L['pos'].extend(corners[i])
        L['uv'].extend(uvs[i])
        k = shades[i] if shades else 1.0
        L['col'].extend((int(color[0] * k), int(color[1] * k), int(color[2] * k)))


AO_LEVELS = (0.45, 0.62, 0.8, 1.0)


@lru_cache(None)
def occludes(state):
    return is_full_opaque(state) or 'leaves' in state


def vertex_ao(x, y, z, face, corner):
    n = FACE_DIR[face]
    ax = [i for i in range(3) if n[i] == 0]
    base = (x + n[0], y + n[1], z + n[2])
    s = [1 if corner[a] >= 8 else -1 for a in ax]
    def solid(d1, d2):
        p = list(base)
        p[ax[0]] += d1
        p[ax[1]] += d2
        return occludes(state_at(*p))
    s1, s2 = solid(s[0], 0), solid(0, s[1])
    c = solid(s[0], s[1])
    level = 0 if (s1 and s2) else 3 - (s1 + s2 + c)
    return AO_LEVELS[level]


missing = set()


def add_block(x, y, z, state):
    name = state.split('[')[0]
    if name in ('minecraft:air', 'minecraft:cave_air', 'minecraft:void_air'):
        return
    if name in ('minecraft:water', 'minecraft:bubble_column'):
        add_water(x, y, z)
        return
    models = block_models(state)
    if not models:
        missing.add(name)
        return
    layer = render_layer(state)
    for model_ref, xr, yr, _uvlock in models:
        elements, tex = resolve_model(model_ref)
        for el in elements:
            f, t = el['from'], el['to']
            r = el.get('rotation')
            for face, fd in el.get('faces', {}).items():
                tname = tex(fd.get('texture', ''))
                if not tname:
                    continue
                cull = fd.get('cullface')
                if cull:
                    wd = rot_dir(FACE_DIR[cull], xr, yr)
                    if wd:
                        dx, dy, dz = FACE_DIR[wd]
                        nb = state_at(x + dx, y + dy, z + dz)
                        if is_full_opaque(nb) or (nb == state and layer != 'opaque' and 'leaves' not in name):
                            continue
                corners = face_corners(face, f, t)
                if r:
                    corners = [rot_point(c, r['axis'], r['angle'], r['origin'], r.get('rescale', False)) for c in corners]
                corners = [rot_block(c, xr, yr) for c in corners]
                world = [(x + c[0] / 16, y + c[1] / 16, z + c[2] / 16) for c in corners]
                u0, v0, u1, v1 = fd.get('uv') or default_uv(face, f, t)
                quad = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
                rot = (fd.get('rotation', 0) // 90) % 4
                quad = quad[rot:] + quad[:rot]
                atlas.slot(tname)
                uvs = [atlas.uv(tname, u, v) for u, v in quad]
                wf = rot_dir(FACE_DIR[face], xr, yr) or face
                shade = SHADE[wf] if el.get('shade', True) else 1.0
                tr, tg, tb = tint_for(state, fd.get('tintindex'))
                color = (int(tr * shade), int(tg * shade), int(tb * shade))
                shades = [vertex_ao(x, y, z, wf, c) for c in corners] if layer != 'translucent' else None
                emit(layer, world, uvs, color, shades)


def add_water(x, y, z):
    above = state_at(x, y + 1, z).split('[')[0]
    h = 1.0 if above in ('minecraft:water', 'minecraft:bubble_column') else 14 / 16
    atlas.slot('block/water_still')
    uv = [atlas.uv('block/water_still', u, v) for u, v in ((0, 0), (16, 0), (16, 16), (0, 16))]
    col = WATER_TINT
    if h < 1:
        emit('translucent', [(x, y + h, z), (x + 1, y + h, z), (x + 1, y + h, z + 1), (x, y + h, z + 1)], uv, col)
    for face, (dx, dy, dz) in FACE_DIR.items():
        if face in ('up', 'down'):
            continue
        nb = state_at(x + dx, y, z + dz)
        if nb.split('[')[0] in ('minecraft:water', 'minecraft:bubble_column') or is_full_opaque(nb):
            continue
        c = face_corners(face, (0, 0, 0), (16, h * 16, 16))
        emit('translucent', [(x + p[0] / 16, y + p[1] / 16, z + p[2] / 16) for p in c], uv, tuple(int(v * 0.8) for v in col))


def main():
    for y in range(SY):
        for z in range(SZ):
            for x in range(SX):
                add_block(x, y, z, PALETTE[DATA[idx(x, y, z)]])
    img = atlas.build()
    # повторно пересчитать uv нельзя — atlas.cols известен только после build, поэтому uv считаем в два прохода
    return img


if __name__ == '__main__':
    # Первый проход: собрать список текстур, чтобы знать размер атласа
    for y in range(SY):
        for z in range(SZ):
            for x in range(SX):
                s = PALETTE[DATA[idx(x, y, z)]]
                for model_ref, *_ in block_models(s):
                    els, tex = resolve_model(model_ref)
                    for el in els:
                        for fd in el.get('faces', {}).values():
                            t = tex(fd.get('texture', ''))
                            if t:
                                atlas.slot(t)
    atlas.slot('block/water_still')
    img = atlas.build()
    img.save(os.path.join(OUT, 'atlas.png'))
    for y in range(SY):
        for z in range(SZ):
            for x in range(SX):
                add_block(x, y, z, PALETTE[DATA[idx(x, y, z)]])

    meta = {'origin': [OX, OY, OZ], 'size': [SX, SY, SZ], 'layers': {}}
    with open(os.path.join(OUT, 'scene.bin'), 'wb') as fb:
        off = 0
        for name, L in layers.items():
            n = len(L['pos']) // 3
            pos = struct.pack(f'<{len(L["pos"])}f', *L['pos'])
            uv = struct.pack(f'<{len(L["uv"])}f', *L['uv'])
            col = bytes(L['col'])
            meta['layers'][name] = {'count': n, 'pos': [off, len(pos)], 'uv': [off + len(pos), len(uv)], 'col': [off + len(pos) + len(uv), len(col)]}
            pad = (-(len(pos) + len(uv) + len(col))) % 4
            fb.write(pos + uv + col + b'\0' * pad)
            off += len(pos) + len(uv) + len(col) + pad
    json.dump(meta, open(os.path.join(OUT, 'scene.json'), 'w'))
    print('треугольников:', {k: len(v['pos']) // 9 for k, v in layers.items()}, 'текстур:', len(atlas.slots), 'без моделей:', sorted(missing)[:20])
