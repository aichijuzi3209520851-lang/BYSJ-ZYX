# -*- coding: utf-8 -*-
"""IotLinkLab（物联网链路调试台）程序图标生成器 —— 终版。

方案：品牌绿渐变圆角底板 + 白色正交信号边沿
      母题 = 串口/示波器里一次信号跳变（低电平 → 上升沿 → 高电平）。
      它既不是箭头、也不是对称双足波形，避免被读成「排序」「桌子」「Π」。

设计依据（24 轮实测，详见 NOTES.md 与 _ref/）：
  · 淡底方案（v2/v3/H 族）在白色资源管理器里视觉重量远低于 EMQX 青色块，
    16px 看起来像空图标 —— 故必须用饱和底板。v1 的错误是字形（通用双箭头），
    不是底板本身。
  · 对称双足（低-高-低且左右等长）在 16px 几乎必被读成「Π / 桌子 / 订书钉」。
    单边信号沿没有对称性，不会被这样读。
  · 实心多边形填充在 16px 反而更差（塌成柱状图/像素点）。

技术：8x 超采样 + LANCZOS 缩小做抗锯齿，不依赖任何外部素材。
      手写 ICO 容器：16..64px 用 32bpp DIB（含真实 1bpp AND mask），
      128/256px 用 PNG frame —— Pillow 自身只写 PNG frame，故不用它的 saver。
"""
import os
import struct
import zlib

from PIL import Image, ImageDraw

OUT = os.path.dirname(os.path.abspath(__file__))

# ---- 品牌色（源：WinFormsApp1/Theme.cs 单一真源）----
GREEN_T = (0x1B, 0x8F, 0x46)   # 略亮于 Primary #15803D，供竖向渐变顶部
GREEN_B = (0x0C, 0x52, 0x28)   # 略深于 PrimaryDark #0E592C，供渐变底部
WHITE = (0xFF, 0xFF, 0xFF)

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
PNG_SIZES = [128, 256]         # 这两档写 PNG frame，其余写 DIB
SS = 8                         # 超采样倍数

TILE_RADIUS = 0.215
# 底板内缩：小尺寸近乎满版，保证 16px 有足够视觉重量
INSET_FOR = {16: 0.010, 24: 0.020, 32: 0.026, 48: 0.032,
             64: 0.036, 128: 0.040, 256: 0.040}
# 线宽提示：小尺寸必须加粗，否则 LANCZOS 缩小后糊成一团
STROKE_FOR = {16: 0.175, 24: 0.160, 32: 0.148, 48: 0.134,
              64: 0.124, 128: 0.112, 256: 0.110}

# 信号边沿几何（归一化 0..1）：左低段 → 上升沿 → 右高段
EDGE_PTS = [(0.165, 0.720), (0.510, 0.720), (0.510, 0.290), (0.835, 0.290)]


def _table(t, size):
    return t.get(size, t[max(t)])


def _vgrad(n, c0, c1):
    """竖向渐变色块（刻意不用 EMQX 那种对角渐变）。"""
    strip = Image.new("RGB", (1, n))
    px = strip.load()
    for y in range(n):
        t = y / max(1, n - 1)
        px[0, y] = tuple(int(round(c0[i] + (c1[i] - c0[i]) * t)) for i in range(3))
    return strip.resize((n, n), Image.BILINEAR).convert("RGBA")


def _tile_mask(n):
    inset = _table(INSET_FOR, n)
    m = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(m)
    a = inset * n
    b = (n - 1) - inset * n
    d.rounded_rectangle([a, a, b, b], radius=int(round(TILE_RADIUS * n)), fill=255)
    return m


def _edge_mask(n):
    """正交信号边沿：粗圆头折线。返回 L 掩码。"""
    w = _table(STROKE_FOR, n)
    m = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(m)
    xy = [(x * n, y * n) for x, y in EDGE_PTS]
    ww = max(1, int(round(w * n)))
    d.line(xy, fill=255, width=ww, joint="curve")
    r = ww / 2.0
    for (x, y) in (xy[0], xy[-1]):
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return m


# LANCZOS 缩小时，圆角外侧会有极淡的 alpha 渗色残留（实测 16px 四角 alpha=2）。
# 0.8% 不透明度肉眼不可见，但会让透明区域带一层灰雾，故统一夹到 0。
_ALPHA_FLOOR = 6


def _clean_alpha(im):
    a = im.getchannel("A").point(lambda v: 0 if v <= _ALPHA_FLOOR else v)
    im.putalpha(a)
    return im


def render(size, scale=SS):
    """渲染单档图标（RGBA）。内部以 size*scale 绘制后 LANCZOS 缩小。"""
    n = size * scale
    im = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    tm = _tile_mask(n)
    im.paste(_vgrad(n, GREEN_T, GREEN_B), (0, 0), tm)
    im.paste(Image.new("RGBA", (n, n), WHITE + (255,)), (0, 0), _edge_mask(n))
    if scale != 1:
        im = im.resize((size, size), Image.LANCZOS)
    return _clean_alpha(im)


# ---------- ICO 容器（手写，兼顾 Windows 兼容性）----------

def _dib_frame(im):
    """32bpp BI_RGB DIB + 真实 1bpp AND mask。biHeight = 2 * 高度。"""
    w, h = im.size
    px = im.load()
    rows = []
    for y in range(h - 1, -1, -1):          # DIB 自底向上
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))       # BGRA
        rows.append(bytes(row))
    pixels = b"".join(rows)

    # AND mask：alpha == 0 的像素置 1（透明）。每行按 4 字节对齐。
    stride = ((w + 31) // 32) * 4
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        bits = bytearray(stride)
        for x in range(w):
            if px[x, y][3] == 0:
                bits[x >> 3] |= 0x80 >> (x & 7)
        mask += bits
    mask = bytes(mask)

    hdr = struct.pack("<IiiHHIIiiII",
                      40,          # biSize
                      w,           # biWidth
                      h * 2,       # biHeight（颜色 + mask 各一份高度）
                      1,           # biPlanes
                      32,          # biBitCount
                      0,           # biCompression = BI_RGB
                      len(pixels) + len(mask),  # biSizeImage
                      0, 0, 0, 0)
    return hdr + pixels + mask


def _png_frame(im):
    """无损 PNG 字节流。im.save 到 BytesIO 即可产出 PNG frame。"""
    import io
    buf = io.BytesIO()
    im.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def write_ico(path, frames):
    """frames: [(size, bytes)]，按尺寸升序写出。"""
    frames = sorted(frames, key=lambda t: t[0])
    count = len(frames)
    header = struct.pack("<HHH", 0, 1, count)        # reserved, type=icon, count
    offset = 6 + 16 * count
    entries = b""
    payload = b""
    for size, data in frames:
        w = 0 if size >= 256 else size               # 256 记作 0
        h = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII",
                               w, h, 0, 0,            # width, height, colors, reserved
                               1, 32,                 # planes, bitcount
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
