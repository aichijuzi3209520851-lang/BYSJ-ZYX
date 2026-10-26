#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
convert_icon.py —— 把 APP-ICON.jpg 转成 Windows 规范的多尺寸透明 app_icon.ico

用途
----
项目里的应用图标母版是一张 JPG（纯白外圈 + 翡翠绿圆角方形主体 Squircle）。
JPG 没有 Alpha 通道，直接改后缀做 .ico 会在任务栏/资源管理器里留一圈白边。
本脚本负责：

  1. 自动识别白底，抠出 Squircle 主体（内部白色装饰线会被完整保留）；
  2. 沿边缘重建**平滑的软 Alpha**（不是硬切的锯齿边）；
  3. 边缘颜色做**外扩填充（alpha bleed）**，用主体本色顶替混了白的像素，彻底消除白边；
  4. 按 Windows 规范生成 256/128/64/48/32/16 六个尺寸、全部 32bpp 的 .ico；
  5. 输出一张对照预览图（浅底 + 深底两行），方便肉眼验收。

依赖
----
    pip install pillow numpy scipy

用法
----
    python convert_icon.py                 # 用脚本同目录的相对路径
    python convert_icon.py -i 源图 -o 输出.ico
    python convert_icon.py --no-trim       # 不裁剪留白，保留母版原始构图
    python convert_icon.py --margin 0.04   # 裁剪后四周留白比例

默认路径（相对脚本所在目录 02-PC-UpperComputer/）：
    输入  ICON/APP-ICON.jpg
    输出  ICON/app_icon.ico   ← 直接覆盖工程里被 csproj 引用的那个文件
"""

from __future__ import annotations

import argparse
import os
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

# ----------------------------------------------------------------------------
# 常量
# ----------------------------------------------------------------------------

#: Windows 标准图标尺寸阶梯（都按 32bpp 输出）
ICON_SIZES = (256, 128, 64, 48, 32, 16)

#: 判定「接近背景色」的容差（0-441，RGB 欧氏距离）。越大吃得越狠，越小越保守。
NEAR_BG_TOL = 28

#: 边缘软 Alpha 的处理带宽（像素）。母版 2048 见方时 4~5px 足够覆盖 JPEG 的抗锯齿。
EDGE_BAND_PX = 4

#: 裁剪主体后四周留白占最终画布的比例。Windows 图标习惯图形占满画布，3%~5% 比较合适。
DEFAULT_MARGIN = 0.03


# ----------------------------------------------------------------------------
# 第 1 步：抠出主体形状
# ----------------------------------------------------------------------------

def estimate_background(rgb: np.ndarray, ring_px: int = 8) -> np.ndarray:
    """取图像最外一圈像素的中位数当作背景色，比写死 (255,255,255) 更耐脏。"""
    ring = np.concatenate([
        rgb[:ring_px].reshape(-1, 3),
        rgb[-ring_px:].reshape(-1, 3),
        rgb[:, :ring_px].reshape(-1, 3),
        rgb[:, -ring_px:].reshape(-1, 3),
    ])
    return np.median(ring, axis=0).astype(np.float32)


def build_opaque_mask(rgb: np.ndarray, bg: np.ndarray) -> np.ndarray:
    """
    返回 bool 掩码：True = Squircle 主体内部（含内部的白色线条细节）。

    思路：先从四边「灌水」找外部背景，再取反。
    这样内部被主体包住的白色装饰线不会被误当成背景挖掉——
    这是本脚本与「把接近白色的像素一律设成透明」那种粗暴做法的关键区别。
    """
    dist = np.linalg.norm(rgb.astype(np.float32) - bg, axis=2)

    # 只把「确实接近背景色」的像素当成可被灌水的地面，
    # 后续 flood 只能在这张网里蔓延，主体内部的深色像素天然是堤坝。
    near_bg = dist <= NEAR_BG_TOL

    # 种子 = 图像最外一圈里所有 near_bg 像素（JPG 边缘可能不是纯色）
    seed = np.zeros_like(near_bg)
    seed[0, :] = near_bg[0, :]
    seed[-1, :] = near_bg[-1, :]
    seed[:, 0] = near_bg[:, 0]
    seed[:, -1] = near_bg[:, -1]
    if not seed.any():                       # 极端情况：四边没有背景色，退化为全图连通域
        seed[0, :] = seed[-1, :] = True
        seed[:, 0] = seed[:, -1] = True

    outside = ndimage.binary_propagation(seed, mask=near_bg)

    opaque = ~outside

    # 去掉孤立噪点（JPEG 压缩偶尔在纯白区留下 1~2 px 脏点）
    opaque = ndimage.binary_opening(opaque, structure=np.ones((3, 3), bool))

    # 只保留最大连通域，避免边缘碎屑变成「飞点」
    labels, count = ndimage.label(opaque)
    if count > 1:
        sizes = ndimage.sum(opaque, labels, range(1, count + 1))
        opaque = labels == (int(np.argmax(sizes)) + 1)

    return opaque


# ----------------------------------------------------------------------------
# 第 2 步：重建软 Alpha + 白底反混合
# ----------------------------------------------------------------------------

def build_rgba(rgb: np.ndarray, opaque: np.ndarray, bg: np.ndarray) -> np.ndarray:
    """
    返回 float32 的 RGBA（0-255）：Alpha 平滑，边缘颜色取自**主体内部**，不含任何背景混色。

    这里不用「反混合（un-multiply）」那种做法。原因：
        反混合要求 Alpha 估计得足够准；一旦 Alpha 略小于真实覆盖率，
        (P - (1-a)·bg)/a 就会把背景色重新加回来，边缘直接白一圈——
        JPG 母版 + 抗锯齿 + 缩放三重误差下，这种白边几乎无法避免。

    改成「颜色外扩（alpha bleed）」：
        * Alpha 只负责形状和透明度，尽量求准；
        * 边缘环带的 RGB 直接用**最近的实心内部像素颜色**填充，
          即把翡翠绿的渐变向轮廓外延展一点点。
    这样即使 Alpha 有偏差，露出来的也只是主体本色，不可能出现白边/灰边。
    """
    rgb_f = rgb.astype(np.float32)
    bg_f = bg.astype(np.float32)

    # --- 1) 确定实心核心区：腐蚀掉边界环带，这里的颜色是干净的 ---
    core = ndimage.binary_erosion(opaque, iterations=EDGE_BAND_PX, border_value=0)
    if not core.any():                        # 图太小，退化为不腐蚀
        core = opaque.copy()

    # 全图每个像素取「最近的干净核心像素」颜色 —— 用于边缘色外扩
    _, indices = ndimage.distance_transform_edt(~core, return_indices=True)
    core_rgb = rgb_f[indices[0], indices[1]]  # shape (h, w, 3)

    # --- 2) Alpha：默认实心 1 / 外部 0，只在边界环带内求软值 ---
    alpha = np.where(opaque, 1.0, 0.0).astype(np.float32)

    grown = ndimage.binary_dilation(opaque, iterations=EDGE_BAND_PX + 1, border_value=0)
    band = grown & ~ndimage.binary_erosion(opaque, iterations=1, border_value=0)

    if band.any():
        # 覆盖率模型：边缘像素 P 是「纯主体色 C」和「背景 bg」的线性混合
        #   P = a·C + (1-a)·bg   ⇒   a = (bg - P) / (bg - C)
        denom = bg_f[None, None, :] - core_rgb
        numer = bg_f[None, None, :] - rgb_f
        with np.errstate(divide="ignore", invalid="ignore"):
            ratio = numer / denom
        # 分母太小说明该通道上「主体色≈背景色」，这条信息不可靠，屏蔽掉
        usable = np.abs(denom) > 24.0
        ratio = np.where(usable, ratio, np.nan)

        with np.errstate(invalid="ignore"):
            band_alpha = np.nanmedian(ratio, axis=2)

        # 通道全部不可靠的像素，退回几何渐变估计
        dist_out = ndimage.distance_transform_edt(~opaque)
        dist_in = ndimage.distance_transform_edt(opaque)
        ramp = np.clip(0.5 + (dist_in - dist_out) / (2.0 * EDGE_BAND_PX), 0.0, 1.0)
        band_alpha = np.where(np.isnan(band_alpha), ramp, band_alpha)
        band_alpha = np.clip(band_alpha, 0.0, 1.0)

        # 轻微平滑：JPG 的边缘色度溢出会让逐像素 Alpha 抖动，表现为毛刺
        band_alpha = ndimage.gaussian_filter(band_alpha, sigma=0.6, mode="nearest")

        alpha = np.where(band, band_alpha, alpha).astype(np.float32)
        alpha[~grown] = 0.0

    np.clip(alpha, 0.0, 1.0, out=alpha)

    # --- 3) 颜色：环带内一律用外扩进来的主体色，彻底切断背景混色 ---
    rgb_out = np.where((alpha[:, :, None] >= 0.999), rgb_f, core_rgb)
    rgb_out = np.clip(rgb_out, 0.0, 255.0)

    return np.dstack([rgb_out, alpha * 255.0]).astype(np.float32)


# ----------------------------------------------------------------------------
# 第 3 步：裁剪 / 留白 / 高质量缩放
# ----------------------------------------------------------------------------

def trim_and_pad(rgba: np.ndarray, margin_ratio: float) -> np.ndarray:
    """裁到主体外接框，再补成正方形，四周按 margin_ratio 留白。"""
    alpha = rgba[:, :, 3]
    ys, xs = np.nonzero(alpha > 8)                 # 忽略近乎全透明的像素
    if len(xs) == 0:
        return rgba

    x0, x1 = int(xs.min()), int(xs.max()) + 1
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    cropped = rgba[y0:y1, x0:x1]

    ch, cw = cropped.shape[:2]
    side = max(ch, cw)
    margin = int(round(side * margin_ratio))
    canvas_side = side + margin * 2

    canvas = np.zeros((canvas_side, canvas_side, 4), dtype=np.float32)
    oy = (canvas_side - ch) // 2
    ox = (canvas_side - cw) // 2
    canvas[oy:oy + ch, ox:ox + cw] = cropped
    return canvas


def resize_rgba(rgba: np.ndarray, size: int) -> Image.Image:
    """
    Alpha 加权（预乘）缩放。

    直接对 RGBA 做 LANCZOS 会把「透明区的黑色 RGB」混进边缘，产生黑边/灰边；
    先预乘再做插值、最后还原，是图标缩小不成脏边唯一正确的做法。
    """
    a = rgba[:, :, 3:4] / 255.0
    premul = np.dstack([rgba[:, :, :3] * a, rgba[:, :, 3]])

    chans = []
    for c in range(4):
        plane = Image.fromarray(premul[:, :, c].astype(np.float32), mode="F")
        plane = plane.resize((size, size), Image.Resampling.LANCZOS)
        chans.append(np.asarray(plane, dtype=np.float32))

    out = np.dstack(chans)
    a_small = np.clip(out[:, :, 3:4] / 255.0, 0.0, 1.0)
    with np.errstate(divide="ignore", invalid="ignore"):
        rgb_small = np.where(a_small > 1e-4, out[:, :, :3] / np.maximum(a_small, 1e-4), 0.0)
    rgb_small = np.clip(rgb_small, 0.0, 255.0)

    result = np.dstack([rgb_small, np.clip(out[:, :, 3], 0.0, 255.0)])
    return Image.fromarray(result.round().astype(np.uint8), mode="RGBA")


# ----------------------------------------------------------------------------
# 第 4 步：手写 ICO（全部 32bpp DIB + AND 掩码，兼容性最好）
# ----------------------------------------------------------------------------

def to_ico_frame(img: Image.Image) -> bytes:
    """
    把单张 RGBA 图编成一帧 ICO 内嵌数据：BITMAPINFOHEADER + BGRA 位图 + AND 掩码。

    说明：
      * ICO 里的 DIB 是**自底向上**存的，必须先垂直翻转；
      * 像素顺序是 BGRA，不是 RGBA；
      * 虽然 32bpp 通常用 Alpha 通道就够，但规范要求 AND 掩码存在，
        缺了它部分老软件（含某些资源编辑器）会认为文件损坏。
    """
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    w, h = img.size
    arr = np.asarray(img, dtype=np.uint8)

    # BGRA + 垂直翻转（自底向上）
    bgra = arr[:, :, [2, 1, 0, 3]][::-1]

    # AND 掩码：1 bit/像素，bit=1 表示透明。每行按 4 字节对齐。
    alpha = arr[:, :, 3][::-1]
    transparent = (alpha < 128).astype(np.uint8)
    row_bytes = ((w + 31) // 32) * 4
    mask = np.zeros((h, row_bytes), dtype=np.uint8)
    for y in range(h):
        bits = transparent[y]
        for x in range(w):
            if bits[x]:
                mask[y, x >> 3] |= 0x80 >> (x & 7)
    mask_bytes = mask.tobytes()

    xor_bytes = bgra.tobytes()
    header = struct.pack(
        "<IiiHHIIiiII",
        40,          # biSize
        w,           # biWidth
        h * 2,       # biHeight = XOR 高度 + AND 高度
        1,           # biPlanes
        32,          # biBitCount
        0,           # biCompression = BI_RGB
        len(xor_bytes) + len(mask_bytes),  # biSizeImage
        0, 0, 0, 0,
    )
    return header + xor_bytes + mask_bytes


def write_ico(path: Path, frames: dict[int, Image.Image]) -> None:
    """按 ICO 规范把多帧拼成一个文件。"""
    entries, blobs = [], []
    offset = 6 + 16 * len(frames)

    for size in sorted(frames):
        blob = to_ico_frame(frames[size])
        entries.append(struct.pack(
            "<BBBBHHII",
            size if size < 256 else 0,   # 256 在目录项里写 0
            size if size < 256 else 0,
            0,                            # 调色板颜色数（32bpp 为 0）
            0,                            # 保留
            1,                            # 平面数
            32,                           # 位深
            len(blob),
            offset,
        ))
        blobs.append(blob)
        offset += len(blob)

    with open(path, "wb") as fp:
        fp.write(struct.pack("<HHH", 0, 1, len(frames)))   # ICONDIR
        for e in entries:
            fp.write(e)
        for b in blobs:
            fp.write(b)


# ----------------------------------------------------------------------------
# 主流程
# ----------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(
        description="把 APP-ICON.jpg 转成透明多尺寸 app_icon.ico",
    )
    parser.add_argument("-i", "--input", type=Path, default=here / "ICON" / "APP-ICON.jpg")
    parser.add_argument("-o", "--output", type=Path, default=here / "ICON" / "app_icon.ico")
    parser.add_argument("--no-trim", action="store_true",
                        help="不裁剪主体周围的白边（保留母版原始构图，图形会显得偏小）")
    parser.add_argument("--margin", type=float, default=DEFAULT_MARGIN,
                        help=f"裁剪后四周留白比例，默认 {DEFAULT_MARGIN}")
    parser.add_argument("--preview", type=Path, default=None,
                        help="额外输出一张尺寸阶梯预览图（默认与 ico 同目录的 app_icon_preview.png）")
    args = parser.parse_args(argv)

    src: Path = args.input
    dst: Path = args.output
    if not src.is_file():
        print(f"[×] 找不到源图: {src}", file=sys.stderr)
        return 1

    print(f"[1/5] 读取源图 {src.name} ...")
    rgb_img = Image.open(src).convert("RGB")
    rgb = np.asarray(rgb_img)
    print(f"      尺寸 {rgb_img.width}×{rgb_img.height}")

    print("[2/5] 识别背景并抠出主体 ...")
    bg = estimate_background(rgb)
    print(f"      背景色 ≈ RGB({bg[0]:.0f}, {bg[1]:.0f}, {bg[2]:.0f})")
    opaque = build_opaque_mask(rgb, bg)
    ratio = opaque.mean() * 100
    print(f"      主体占比 {ratio:.1f}%")
    if ratio < 3 or ratio > 99:
        print("[×] 主体识别异常（占比过低或过高），请检查源图背景是否为纯色", file=sys.stderr)
        return 2

    print("[3/5] 重建软 Alpha 并去除白边 ...")
    rgba = build_rgba(rgb, opaque, bg)
    if args.no_trim:
        working = rgba
        print("      --no-trim：保留原始构图")
    else:
        working = trim_and_pad(rgba, args.margin)
        print(f"      裁剪 + 留白后画布 {working.shape[1]}×{working.shape[0]}")

    print("[4/5] 生成尺寸阶梯（预乘 Alpha 缩放）...")
    frames: dict[int, Image.Image] = {}
    for size in ICON_SIZES:
        frames[size] = resize_rgba(working, size)
        print(f"      {size}×{size} 完成")

    dst.parent.mkdir(parents=True, exist_ok=True)

    # 覆盖前先把旧图标另存为 app_icon_old.ico（只备份第一次，避免把备份也冲掉）
    if dst.is_file():
        legacy = dst.with_name("app_icon_old.ico")
        if not legacy.exists():
            legacy.write_bytes(dst.read_bytes())
            print(f"      已备份旧图标 → {legacy.name}")
        else:
            print(f"      旧图标备份已存在（{legacy.name}），本次不再覆盖备份")

    print("[5/5] 打包 ICO ...")
    write_ico(dst, frames)
    kb = dst.stat().st_size / 1024
    print(f"[√] 已生成 {dst}  ({kb:.1f} KB, {len(frames)} 帧 32bpp)")

    preview_path = args.preview or dst.with_name("app_icon_preview.png")
    make_preview(frames, preview_path)
    print(f"[√] 预览图 {preview_path}")
    return 0


def make_preview(frames: dict[int, Image.Image], path: Path) -> None:
    """把六个尺寸并排铺在深色/浅色两种底色上，肉眼验收白边与辨识度。"""
    ordered = sorted(frames, reverse=True)
    pad = 16
    cell_w = max(ordered) + pad * 2
    total_w = cell_w * len(ordered)
    cell_h = max(ordered) + pad * 2
    total_h = cell_h * 2

    canvas = Image.new("RGB", (total_w, total_h), (255, 255, 255))
    # 上半浅底、下半深底，两种主题下都要没有白边/黑边
    dark = Image.new("RGB", (total_w, cell_h), (32, 40, 48))
    canvas.paste(dark, (0, cell_h))

    for row in range(2):
        for col, size in enumerate(ordered):
            tile = frames[size]
            x = col * cell_w + (cell_w - size) // 2
            y = row * cell_h + (cell_h - size) // 2
            canvas.paste(tile, (x, y), tile)

    canvas.save(path)


if __name__ == "__main__":
    raise SystemExit(main())
