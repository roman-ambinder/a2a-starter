"""Writes eng/icon.png: two filled circles (agents) joined by a thick line, 128x128. Standard library only."""
import struct, sys, zlib

SIZE, SS = 128, 4  # SS x SS samples per pixel for smooth edges
BG, FG, ACCENT = (24, 44, 84), (245, 247, 250), (255, 176, 46)
NODES = [(36.0, 64.0), (92.0, 64.0)]
R, HALF = 22.0, 6.0


def covered(x, y):
    (x1, y1), (x2, y2) = NODES
    if x1 <= x <= x2 and abs(y - y1) <= HALF:
        return FG
    for i, (cx, cy) in enumerate(NODES):
        if (x - cx) ** 2 + (y - cy) ** 2 <= R * R:
            return ACCENT if i else FG
    return None


rows = []
for py in range(SIZE):
    row = bytearray([0])
    for px in range(SIZE):
        acc = [0, 0, 0]
        for sy in range(SS):
            for sx in range(SS):
                c = covered(px + (sx + 0.5) / SS, py + (sy + 0.5) / SS) or BG
                for k in range(3):
                    acc[k] += c[k]
        row += bytes(v // (SS * SS) for v in acc)
    rows.append(bytes(row))


def chunk(kind, data):
    body = kind + data
    return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)


png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
       + chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + chunk(b"IEND", b""))
open(sys.argv[1] if len(sys.argv) > 1 else "eng/icon.png", "wb").write(png)
