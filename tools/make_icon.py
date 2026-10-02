#!/usr/bin/env python3
"""RingInfo のアプリアイコンを元画像（tools/icon-source.png）から生成する。外部ライブラリ不要。

使い方: python3 tools/make_icon.py
  → src/RingInfo.App/Assets/RingInfo.ico（16〜256px の複数サイズ）
  → src/RingInfo.App/Assets/RingInfo.png（画面表示用 256px）

元画像は 8bit RGBA・インターレースなしの正方形 PNG（1024px 推奨）。
"""
import os
import struct
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "icon-source.png")
ASSETS = os.path.join(HERE, "..", "src", "RingInfo.App", "Assets")
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def read_png(path):
    """8bit RGBA の PNG を読み込み、(幅, 高さ, 行ごとの bytearray) を返す"""
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "PNG ではありません"
    pos, idat = 8, b""
    while pos < len(data):
        length, tag = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if tag == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            assert depth == 8 and color == 6 and interlace == 0, "8bit RGBA・インターレースなしの PNG のみ対応"
        elif tag == b"IDAT":
            idat += body
        pos += 12 + length

    raw = zlib.decompress(idat)
    bpp, stride = 4, width * 4
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(height):
        kind = raw[i]
        line = bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        if kind == 1:
            for x in range(bpp, stride):
                line[x] = (line[x] + line[x - bpp]) & 0xFF
        elif kind == 2:
            for x in range(stride):
                line[x] = (line[x] + prev[x]) & 0xFF
        elif kind == 3:
            for x in range(stride):
                left = line[x - bpp] if x >= bpp else 0
                line[x] = (line[x] + ((left + prev[x]) >> 1)) & 0xFF
        elif kind == 4:
            for x in range(stride):
                a = line[x - bpp] if x >= bpp else 0
                b = prev[x]
                c = prev[x - bpp] if x >= bpp else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                line[x] = (line[x] + pred) & 0xFF
        rows.append(line)
        prev = line
    return width, height, rows


def to_premultiplied(width, height, rows):
    """面積平均で縮小するため、アルファを掛けた浮動小数の配列にする（透明部分の色がにじまない）"""
    pixels = []
    for line in rows:
        out = []
        for x in range(width):
            r, g, b, a = line[x * 4:x * 4 + 4]
            f = a / 255.0
            out.append((r * f, g * f, b * f, float(a)))
        pixels.append(out)
    return pixels


def resize(pixels, size):
    """面積平均による縮小（正方形 → 正方形）"""
    src = len(pixels)
    scale = src / size

    def spans(n):
        result = []
        for i in range(n):
            start, end = i * scale, (i + 1) * scale
            parts = []
            j = int(start)
            while j < end and j < src:
                weight = min(end, j + 1) - max(start, j)
                if weight > 0:
                    parts.append((j, weight))
                j += 1
            result.append(parts)
        return result

    cols = spans(size)
    # 横方向
    horizontal = []
    for line in pixels:
        out = []
        for parts in cols:
            acc = [0.0, 0.0, 0.0, 0.0]
            for j, w in parts:
                p = line[j]
                acc[0] += p[0] * w
                acc[1] += p[1] * w
                acc[2] += p[2] * w
                acc[3] += p[3] * w
            out.append([v / scale for v in acc])
        horizontal.append(out)
    # 縦方向
    result = []
    for parts in cols:
        out = []
        for x in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for j, w in parts:
                p = horizontal[j][x]
                acc[0] += p[0] * w
                acc[1] += p[1] * w
                acc[2] += p[2] * w
                acc[3] += p[3] * w
            out.append(tuple(v / scale for v in acc))
        result.append(out)
    return result


def encode_png(pixels):
    size = len(pixels)
    raw = bytearray()
    for line in pixels:
        raw.append(0)
        for r, g, b, a in line:
            if a <= 0:
                raw.extend((0, 0, 0, 0))
                continue
            f = 255.0 / a
            raw.extend((min(255, round(r * f)), min(255, round(g * f)), min(255, round(b * f)), min(255, round(a))))

    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")


def main():
    width, height, rows = read_png(SOURCE)
    assert width == height, "正方形の画像を指定してください"
    pixels = to_premultiplied(width, height, rows)

    # まず 256px にしてから、小さいサイズはそこから縮小する（処理時間の短縮）
    base = resize(pixels, 256) if width != 256 else pixels
    images = {size: encode_png(base if size == 256 else resize(base, size)) for size in ICO_SIZES}

    ico = struct.pack("<HHH", 0, 1, len(ICO_SIZES))
    offset = 6 + 16 * len(ICO_SIZES)
    for size in ICO_SIZES:
        dim = 0 if size >= 256 else size
        ico += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(images[size]), offset)
        offset += len(images[size])
    ico += b"".join(images[size] for size in ICO_SIZES)

    ico_path = os.path.normpath(os.path.join(ASSETS, "RingInfo.ico"))
    png_path = os.path.normpath(os.path.join(ASSETS, "RingInfo.png"))
    open(ico_path, "wb").write(ico)
    open(png_path, "wb").write(images[256])
    print("generated", ico_path, len(ico), "bytes")
    print("generated", png_path, len(images[256]), "bytes")


if __name__ == "__main__":
    main()
