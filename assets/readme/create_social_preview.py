"""Render the 1280x640 GitHub social preview from the README screenshots."""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT / "social-preview.jpg"
SIZE = (1280, 640)
BG = (8, 16, 29)
MINT = (112, 225, 190)
WHITE = (245, 249, 252)
MUTED = (186, 202, 214)


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    path = Path(r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc")
    if not path.exists():
        path = Path(r"C:\Windows\Fonts\segoeuib.ttf" if bold else r"C:\Windows\Fonts\segoeui.ttf")
    return ImageFont.truetype(str(path), size)


def cover(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    width, height = size
    scale = max(width / image.width, height / image.height)
    resized = image.resize((round(image.width * scale), round(image.height * scale)), Image.Resampling.LANCZOS)
    left = (resized.width - width) // 2
    top = (resized.height - height) // 2
    return resized.crop((left, top, left + width, top + height))


def rounded_card(canvas: Image.Image, image: Image.Image, box: tuple[int, int, int, int], radius: int) -> None:
    x, y, width, height = box
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    shadow_draw.rounded_rectangle((x + 4, y + 10, x + width + 4, y + height + 10), radius, fill=(0, 0, 0, 155))
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    canvas.alpha_composite(shadow)

    image = cover(image.convert("RGB"), (width, height)).convert("RGBA")
    mask = Image.new("L", (width, height), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, width - 1, height - 1), radius, fill=255)
    image.putalpha(mask)
    canvas.alpha_composite(image, (x, y))
    ImageDraw.Draw(canvas).rounded_rectangle((x, y, x + width - 1, y + height - 1), radius, outline=(255, 255, 255, 85), width=2)


def main() -> None:
    canvas = cover(Image.open(ROOT / "home.jpg"), SIZE).convert("RGBA")
    canvas.alpha_composite(Image.new("RGBA", SIZE, (4, 12, 23, 105)))

    # A dark left-to-right gradient keeps the text readable while leaving the home UI visible.
    gradient = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    pixels = gradient.load()
    for x in range(SIZE[0]):
        alpha = round(242 * max(0.0, min(1.0, 1 - x / 850)) ** 0.72)
        for y in range(SIZE[1]):
            pixels[x, y] = (BG[0], BG[1], BG[2], alpha)
    canvas.alpha_composite(gradient)

    rounded_card(canvas, Image.open(ROOT / "home.jpg"), (570, 38, 660, 564), 22)
    rounded_card(canvas, Image.open(ROOT / "detail.jpg"), (892, 345, 316, 270), 17)

    draw = ImageDraw.Draw(canvas)
    icon = Image.open(ROOT.parent.parent / "assets" / "app-icon.png").convert("RGBA")
    icon.thumbnail((54, 54), Image.Resampling.LANCZOS)
    canvas.alpha_composite(icon, (70, 64))
    draw = ImageDraw.Draw(canvas)
    draw.text((138, 67), "Momoka", font=font(43, bold=True), fill=WHITE)
    draw.text((72, 166), "Windows 媒体客户端", font=font(24, bold=True), fill=MINT)
    draw.text((70, 212), "播放、订阅、下载", font=font(38, bold=True), fill=WHITE)
    draw.text((70, 262), "刮削与文件管理", font=font(38, bold=True), fill=WHITE)
    draw.rounded_rectangle((72, 330, 422, 334), radius=2, fill=MINT)
    draw.text((72, 360), "媒体库与 MoviePilot 工作流", font=font(20), fill=MUTED)

    labels = ["Emby", "Jellyfin", "飞牛影视", "Plex*"]
    x = 72
    y = 410
    for label in labels:
        box = draw.textbbox((0, 0), label, font=font(16, bold=True))
        width = box[2] - box[0] + 30
        draw.rounded_rectangle((x, y, x + width, y + 34), radius=16, fill=(28, 43, 58, 230), outline=(255, 255, 255, 45), width=1)
        draw.text((x + 15, y + 7), label, font=font(16, bold=True), fill=WHITE)
        x += width + 10

    draw.text((72, 474), "Windows 10 1809+ · x64", font=font(17), fill=MUTED)
    draw.text((72, 536), "* Plex 支持为实验性，尚未完成真实服务器验证", font=font(13), fill=(202, 214, 222))

    canvas.convert("RGB").save(OUTPUT, format="JPEG", quality=86, optimize=True, progressive=True)
    result = Image.open(OUTPUT)
    print(f"Created {OUTPUT} ({result.width}x{result.height}, {OUTPUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
