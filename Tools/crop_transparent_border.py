#!/usr/bin/env python3
"""Trim the transparent border of an RGBA PNG to its alpha bounding box (pure stdlib, no PIL).

This is the "crop out the whitespace so the icon fits perfectly" step for transparent art:
ability icons (Assets/Art/Abilities/icon_*.png) and the menu currency icons
(Assets/Resources/Menu/coin.png, heart.png). See images.md.

  python3 Tools/crop_transparent_border.py in.png out.png            # tight bounding box
  python3 Tools/crop_transparent_border.py --square in.png out.png   # pad bbox to a centred square

--square keeps a round/centred emblem from distorting when the UI slot is square; without it the
crop is as tight as the art allows. ALPHA_THRESHOLD ignores near-transparent anti-alias fringe.
"""
import struct, zlib, sys

ALPHA_THRESHOLD = 8


def read_png(path):
    with open(path, "rb") as f:
        data = f.read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    pos, width, height, idat = 8, None, None, bytearray()
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        ctype = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + length]
        if ctype == b"IHDR":
            width, height, bitdepth, colortype, _, _, interlace = struct.unpack(">IIBBBBB", chunk)
            assert bitdepth == 8 and colortype == 6, f"need 8-bit RGBA (depth={bitdepth} colortype={colortype})"
            assert interlace == 0, "interlaced PNG unsupported"
        elif ctype == b"IDAT":
            idat += chunk
        elif ctype == b"IEND":
            break
        pos += 12 + length
    return width, height, defilter(zlib.decompress(bytes(idat)), width, height)


def defilter(raw, width, height):
    bpp, stride = 4, width * 4
    out, prev, pos = bytearray(stride * height), bytearray(stride), 0
    for y in range(height):
        ftype = raw[pos]; pos += 1
        line = bytearray(raw[pos:pos + stride]); pos += stride
        if ftype == 1:
            for i in range(bpp, stride): line[i] = (line[i] + line[i - bpp]) & 0xFF
        elif ftype == 2:
            for i in range(stride): line[i] = (line[i] + prev[i]) & 0xFF
        elif ftype == 3:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ftype == 4:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                b = prev[i]; c = prev[i - bpp] if i >= bpp else 0
                p = a + b - c; pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return out


def alpha_bbox(pix, width, height):
    minx, miny, maxx, maxy = width, height, -1, -1
    for y in range(height):
        row = y * width * 4
        for x in range(width):
            if pix[row + x * 4 + 3] > ALPHA_THRESHOLD:
                minx, maxx = min(minx, x), max(maxx, x)
                miny, maxy = min(miny, y), max(maxy, y)
    return minx, miny, maxx, maxy


def encode_png(pix, width, height):
    stride = width * 4
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        raw += pix[y * stride:(y + 1) * stride]
    comp = zlib.compress(bytes(raw), 9)

    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", comp) + chunk(b"IEND", b""))


def main(argv):
    square = "--square" in argv
    args = [a for a in argv if not a.startswith("--")]
    src, dst = args[0], args[1]
    w, h, pix = read_png(src)
    minx, miny, maxx, maxy = alpha_bbox(pix, w, h)
    assert maxx >= 0, "image is fully transparent"
    cw, ch = maxx - minx + 1, maxy - miny + 1
    ow, oh = (max(cw, ch), max(cw, ch)) if square else (cw, ch)
    ox, oy = (ow - cw) // 2, (oh - ch) // 2
    out = bytearray(ow * oh * 4)
    for y in range(ch):
        srow = ((miny + y) * w + minx) * 4
        drow = ((oy + y) * ow + ox) * 4
        out[drow:drow + cw * 4] = pix[srow:srow + cw * 4]
    with open(dst, "wb") as f:
        f.write(encode_png(out, ow, oh))
    print(f"{w}x{h} -> bbox {cw}x{ch} (trim L={minx} R={w-1-maxx} T={miny} B={h-1-maxy}) -> {ow}x{oh}{' square' if square else ''}")


if __name__ == "__main__":
    main(sys.argv[1:])
