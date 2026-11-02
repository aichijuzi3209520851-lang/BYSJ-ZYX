# -*- coding: utf-8 -*-
"""IotLinkLab（物联网链路调试台）程序图标生成器 v6 —— 终版「全息链路」。

方案：深空紫底 + 青紫渐变霓虹六边形 + 白色脉冲链路线 + 双层辉光
母题：六边形 = 十六进制 / IoT 节点 / 协议帧；白色折线 = 链路上的信号脉冲。
与 v4/v5（绿底信号沿）完全不同的样式方向 —— 深底霓虹「炫酷」风。

设计要点（v6 探索两轮实测）：
  · 深空底板：#2A2063 → #0A0722 三段纵向渐变（<=32px 用更亮的窄域）
  · 霓虹六边形：青→蓝→紫横向渐变描边，>=32px 加紫色外辉光
  · 白色脉冲线：256px 是「低→升→回落→高」四点链路波形；
    <=32px 简化为「低→升→高」三段（16px 下四点会挤成团）
  · 小尺寸铁律：16px 六边形必须外扩到 R=0.40 且脉冲线短而粗，
    否则六边形塌成「O」环、脉冲糊成一团（v6 第一轮实测教训）
  · 双层辉光：外层宽模糊紫光 + 内层窄模糊青光，只给 >=32px

技术：8x 超采样 + LANCZOS 缩小；手写 ICO 容器（16..64 用 32bpp DIB + 1bpp AND
mask，128/256 用 PNG frame）—— 与 v4/v5 相同，兼容性结论直接继承。
仅依赖 Pillow。
"""
import io
import math
import os
import struct

from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = os.path.dirname(os.path.abspath(__file__))

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
PNG_SIZES = [128, 256]
SS = 8
TILE_RADIUS = 0.215
_ALPHA_FLOOR = 6

# ---- 深空底板（纵向三段渐变） ----
BG_FULL = [(0.00, (0x2A, 0x20, 0x63)), (0.55, (0x17, 0x10, 0x3C)), (1.00, (0x0A, 0x07, 0x22))]
BG_SMALL = [(0.00, (0x33, 0x27, 0x74)), (1.00, (0x10, 0x0B, 0x2E))]

# ---- 霓虹渐变（横向：青 → 蓝 → 紫） ----
GRAD_HEX = [(0.00, (0x00, 0xF0, 0xFF)), (0.55, (0x35, 0x9C, 0xFF)), (1.00, (0xB4, 0x6B, 0xFF))]
GRAD_HEX_BRIGHT = [(0.00, (0x2A, 0xFA, 0xFF)), (0.55, (0x4F, 0xAC, 0xFF)), (1.00, (0xC7, 0x83, 0xFF))]

# ---- 六边形几何（归一化） ----
# 16px 必须外扩（第一轮实测 0.36 会塌成 O 环），且描边显著加粗
R_HEX = {16: 0.400, 24: 0.385, 32: 0.370, 48: 0.350, 64: 0.340, 128: 0.335, 256: 0.330}
STROKE_HEX = {16: 0.150, 24: 0.128, 32: 0.108, 48: 0.080, 64: 0.067, 128: 0.056, 256: 0.052}

# ---- 脉冲链路线 ----
# 大尺寸：低 → 升 → 回落 → 高（链路波形）；小尺寸：低 → 升 → 高（三段简形）
PULSE_FULL = {16: None, 24: None, 32: None,
              48: [(0.275, 0.660), (0.455, 0.660), (0.585, 0.305), (0.740, 0.450)],
              64: [(0.270, 0.665), (0.455, 0.665), (0.585, 0.295), (0.745, 0.445)],
              128: [(0.265, 0.670), (0.455, 0.670), (0.585, 0.285), (0.750, 0.440)],
              256: [(0.260, 0.680), (0.455, 0.680), (0.585, 0.275), (0.760, 0.430)]}
PULSE_SMALL = {16: [(0.245, 0.640), (0.545, 0.640), (0.745, 0.340)],
               24: [(0.270, 0.635), (0.540, 0.635), (0.730, 0.355)],
               32: [(0.285, 0.640), (0.500, 0.640), (0.690, 0.360)]}
STROKE_PULSE = {16: 0.110, 24: 0.098, 32: 0.090, 48: 0.066, 64: 0.056, 128: 0.047, 256: 0.044}

GLOW_HEX_COLOR = (0x7A, 0x4F, 0xFF)
GLOW_PULSE_COLOR = (0x00, 0xD0, 0xFF)
PULSE_FILL = (0xF6, 0xFD, 0xFF)


def _table(t, size):
    return t[size]


def _grad_color(stops, d):
    d = max(0.0, min(1.0, d))
    for i in range(len(stops) - 1):
        t0, c0 = stops[i]
        t1, c1 = stops[i + 1]
        if t0 <= d <= t1:
            f = (d - t0) / (t1 - t0) if t1 > t0 else 0.0
            return tuple(int(round(c0[k] + (c1[k] - c0[k]) * f)) for k in range(3))
    return stops[-1][1]


def vgrad(n, stops):
    col = Image.new("RGB", (1, n))
    p = col.load()
    for y in range(n):
        p[0, y] = _grad_color(stops, y / max(1, n - 1))
    return col.resize((n, n), Image.NEAREST)


def hgrad(n, stops):
    row = Image.new("RGB", (n, 1))
    p = row.load()
    for x in range(n):
        p[x, 0] = _grad_color(stops, x / max(1, n - 1))
    return row.resize((n, n), Image.NEAREST)


def tile_mask(n):
    m = Image.new("L", (n, n), 0)
    ImageDraw.Draw(m).rounded_rectangle(
        [0, 0, n - 1, n - 1], radius=int(round(TILE_RADIUS * n)), fill=255)
    return m


def hexpts(cx, cy, R):
    return [(cx + R * math.cos(math.radians(60 * i - 90)),
             cy + R * math.sin(math.radians(60 * i - 90))) for i in range(6)]


def hex_layer(n, size):
    lw = max(1, int(round(STROKE_HEX[size] * n)))
    m = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(m)
    pts = hexpts(n / 2.0, n / 2.0, R_HEX[size] * n)
    d.line(pts + [pts[0]], fill=255, width=lw, joint="curve")
    r = lw / 2.0
    for x, y in pts:
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return m


def pulse_layer(n, size):
    pts_norm = PULSE_FULL[size] if size >= 48 else PULSE_SMALL[size]
    lw = max(1, int(round(STROKE_PULSE[size] * n)))
    m = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(m)
    xy = [(x * n, y * n) for x, y in pts_norm]
    d.line(xy, fill=255, width=lw, joint="curve")
    r = lw / 2.0
    for x, y in (xy[0], xy[-1]):
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return m


def paste_glow(im, layer, color, blur, gain):
    g = layer.filter(ImageFilter.GaussianBlur(blur))
    g = g.point(lambda v: int(v * gain))
    im.paste(Image.new("RGBA", im.size, color + (255,)), (0, 0), g)


def clean_alpha(im):
    a = im.getchannel("A").point(lambda v: 0 if v <= _ALPHA_FLOOR else v)
    im.putalpha(a)
    return im


def render(size, scale=SS):
    """渲染单档图标（RGBA）：深空底板 + 霓虹六边形 + 辉光 + 白色脉冲线。"""
    n = size * scale
    im = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    tm = tile_mask(n)
    im.paste(vgrad(n, BG_SMALL if size <= 32 else BG_FULL).convert("RGBA"), (0, 0), tm)

    hm = hex_layer(n, size)
    pm = pulse_layer(n, size)
    hmt = ImageChops.multiply(hm, tm)

    if size >= 32:  # 双层辉光只给 >=32px（更小只会变泥）
        paste_glow(im, hmt, GLOW_HEX_COLOR, 0.020 * n, 0.85)
        paste_glow(im, ImageChops.multiply(pm, tm), GLOW_PULSE_COLOR, 0.014 * n, 0.80)

    fill = hgrad(n, GRAD_HEX_BRIGHT if size <= 32 else GRAD_HEX).convert("RGBA")
    im.paste(fill, (0, 0), hmt)
    im.paste(Image.new("RGBA", (n, n), PULSE_FILL + (255,)),
             (0, 0), ImageChops.multiply(pm, tm))

    if scale != 1:
        im = im.resize((size, size), Image.LANCZOS)
    return clean_alpha(im)


# ---------- ICO 容器（手写，与 v4/v5 相同的兼容性结论） ----------

def _dib_frame(im):
    w, h = im.size
    px = im.load()
    rows = []
    for y in range(h - 1, -1, -1):
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    pixels = b"".join(rows)
    stride = ((w + 31) // 32) * 4
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        bits = bytearray(stride)
        for x in range(w):
            if px[x, y][3] == 0:
                bits[x >> 3] |= 0x80 >> (x & 7)
        mask += bits
    hdr = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0,
                      len(pixels) + len(mask), 0, 0, 0, 0)
    return hdr + pixels + bytes(mask)


def _png_frame(im):
    buf = io.BytesIO()
    im.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def write_ico(path, frames):
    frames = sorted(frames, key=lambda t: t[0])
    count = len(frames)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries = b""
    payload = b""
    for size, data in frames:
        w = 0 if size >= 256 else size
        h = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32,
                               len(data), offset)
        offset += len(data)
        payload += data
    with open(path, "wb") as f:
        f.write(header + entries + payload)
    return len(header) + len(entries) + len(payload)


def main():
    frames = []
    for s in ICO_SIZES:
        im = render(s)
        data = _png_frame(im) if s in PNG_SIZES else _dib_frame(im)
        frames.append((s, data))
        print(f"  size {s:>3}px  frame {len(data):>7} bytes "
              f"({'PNG' if s in PNG_SIZES else 'DIB'})")

    ico = os.path.join(OUT, "app.ico")
    total = write_ico(ico, frames)
    print(f"wrote {ico}  {total} bytes  ({len(ICO_SIZES)} sizes)")

    for s in (256, 48, 32, 16):
        p = os.path.join(OUT, f"preview-{s}.png")
        render(s).save(p)
        print("wrote", p)

    for s in (16, 32):
        z = render(s).resize((s * 8, s * 8), Image.NEAREST)
        p = os.path.join(OUT, f"preview-{s}-zoom.png")
        z.save(p)
        print("wrote", p, z.size)


if __name__ == "__main__":
    main()
