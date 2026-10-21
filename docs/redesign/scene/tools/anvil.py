"""Минимальное чтение мира Minecraft (Anvil, 1.18+): блоки по координатам из region/*.mca."""
import math
import os
import struct
import zlib
import gzip


def _rd(b, o, t):
    fmt = {1: '>b', 2: '>h', 3: '>i', 4: '>q', 5: '>f', 6: '>d'}
    if t in fmt:
        return struct.unpack_from(fmt[t], b, o)[0], o + struct.calcsize(fmt[t])
    if t == 7:
        n, = struct.unpack_from('>i', b, o)
        return b[o + 4:o + 4 + n], o + 4 + n
    if t == 8:
        n, = struct.unpack_from('>H', b, o)
        return b[o + 2:o + 2 + n].decode('utf8', 'replace'), o + 2 + n
    if t == 9:
        et = b[o]
        n, = struct.unpack_from('>i', b, o + 1)
        o += 5
        out = []
        for _ in range(n):
            v, o = _rd(b, o, et)
            out.append(v)
        return out, o
    if t == 10:
        d = {}
        while True:
            tt = b[o]
            o += 1
            if tt == 0:
                return d, o
            n, = struct.unpack_from('>H', b, o)
            name = b[o + 2:o + 2 + n].decode('utf8', 'replace')
            o += 2 + n
            v, o = _rd(b, o, tt)
            d[name] = v
    if t == 11:
        n, = struct.unpack_from('>i', b, o)
        return list(struct.unpack_from('>%di' % n, b, o + 4)), o + 4 + 4 * n
    if t == 12:
        n, = struct.unpack_from('>i', b, o)
        return struct.unpack_from('>%dq' % n, b, o + 4), o + 4 + 8 * n
    raise ValueError(t)


def parse_nbt(b):
    n, = struct.unpack_from('>H', b, 1)
    return _rd(b, 3 + n, 10)[0]


def state_key(entry):
    """'minecraft:oak_stairs[facing=east,half=bottom]' — имя и свойства блока."""
    name = entry['Name']
    props = entry.get('Properties')
    if props:
        name += '[' + ','.join(f'{k}={v}' for k, v in sorted(props.items())) + ']'
    return name


class World:
    def __init__(self, region_dir):
        self.region_dir = region_dir
        self._chunks = {}

    def _load_chunk(self, cx, cz):
        key = (cx, cz)
        if key in self._chunks:
            return self._chunks[key]
        rx, rz = cx >> 5, cz >> 5
        path = os.path.join(self.region_dir, f'r.{rx}.{rz}.mca')
        sections = None
        if os.path.exists(path):
            with open(path, 'rb') as f:
                header = f.read(4096)
                idx = 4 * ((cx & 31) + (cz & 31) * 32)
                loc = int.from_bytes(header[idx:idx + 3], 'big')
                if loc:
                    f.seek(loc * 4096)
                    length, comp = struct.unpack('>IB', f.read(5))
                    raw = f.read(length - 1)
                    data = zlib.decompress(raw) if comp == 2 else gzip.decompress(raw)
                    chunk = parse_nbt(data)
                    sections = {}
                    for sec in chunk.get('sections', []):
                        bs = sec.get('block_states')
                        if not bs:
                            continue
                        palette = [state_key(p) for p in bs['palette']]
                        longs = bs.get('data')
                        sections[sec['Y']] = (palette, longs)
        self._chunks[key] = sections
        return sections

    def block(self, x, y, z):
        sections = self._load_chunk(x >> 4, z >> 4)
        if not sections:
            return None
        sec = sections.get(y >> 4)
        if not sec:
            return 'minecraft:air'
        palette, longs = sec
        if longs is None or len(palette) == 1:
            return palette[0]
        bits = max(4, math.ceil(math.log2(len(palette))))
        per_long = 64 // bits
        i = ((y & 15) * 256) + ((z & 15) * 16) + (x & 15)
        v = longs[i // per_long] & 0xFFFFFFFFFFFFFFFF
        v = (v >> ((i % per_long) * bits)) & ((1 << bits) - 1)
        return palette[v] if v < len(palette) else 'minecraft:air'
