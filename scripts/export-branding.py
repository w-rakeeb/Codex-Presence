import argparse
import hashlib
import json
from pathlib import Path
from shutil import copyfile

from PIL import Image, ImageDraw, ImageFont


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--delivery", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    master = Image.open(args.source).convert("RGBA")
    if master.size != (1024, 1024):
        raise ValueError("Use the approved square 1024px source.")
    delivery = args.delivery.resolve()
    delivery.mkdir(parents=True, exist_ok=True)
    assets = root / "desktop" / "Assets"
    branding = root / "assets" / "branding"
    sizes = [16, 20, 24, 32, 48, 64, 128, 256, 512, 1024]
    frames = sizes[:8]
    copyfile(args.source, branding / "codex-presence.png")
    copyfile(args.source, branding / "codex-app.png")
    copyfile(args.source, delivery / "approved-master.png")
    for size in sizes:
        image = master.resize((size, size), Image.Resampling.LANCZOS)
        image.save(delivery / f"logo-{size}.png")
        if size in [16, 20, 24, 32, 48, 256]:
            image.save(assets / f"logo-{size}.png")
    master.save(assets / "app.ico", sizes=[(size, size) for size in frames])
    copyfile(assets / "app.ico", root / "desktop" / "app.ico")
    copyfile(assets / "app.ico", delivery / "app.ico")
    for theme in ["", "-light"]:
        for state, color in {"neutral": None, "live": "#57D68D", "paused": "#F0B232", "error": "#ED4245"}.items():
            icon = master.copy()
            if color:
                draw = ImageDraw.Draw(icon)
                draw.ellipse((650, 650, 1020, 1020), fill=color, outline="#17181B", width=48)
            filename = f"tray-{state}{theme}.ico"
            icon.save(assets / filename, sizes=[(size, size) for size in frames])
            copyfile(assets / filename, delivery / filename)
    font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 78)
    for name, height in [("codex-presence-banner.png", 260), ("social-card.png", 630)]:
        canvas = Image.new("RGBA", (1200, height), "#0F1012")
        if height == 260:
            canvas.alpha_composite(master.resize((176, 176), Image.Resampling.LANCZOS), (40, 42))
            ImageDraw.Draw(canvas).text((252, 130), "Codex Presence", font=font, fill="#ECEDEF", anchor="lm")
        else:
            canvas.alpha_composite(master.resize((280, 280), Image.Resampling.LANCZOS), (460, 92))
            ImageDraw.Draw(canvas).text((600, 465), "Codex Presence", font=font, fill="#ECEDEF", anchor="mm")
        canvas.convert("RGB").save(branding / name)
        copyfile(branding / name, delivery / name)
    preview = Image.new("RGB", (1000, 320), "#17181B")
    draw = ImageDraw.Draw(preview)
    small_font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 18)
    x = 24
    for size in [16, 20, 24, 32, 48, 64, 128, 256]:
        preview.paste(master.resize((size, size), Image.Resampling.LANCZOS), (x, 24), master.resize((size, size), Image.Resampling.LANCZOS))
        draw.text((x, 290), str(size), fill="#ECEDEF", font=small_font)
        x += size + 26
    preview.save(delivery / "size-review.png")
    files = {file.name: hashlib.sha256(file.read_bytes()).hexdigest() for file in sorted(delivery.iterdir()) if file.is_file() and file.name != "manifest.json"}
    (delivery / "manifest.json").write_text(json.dumps({"source": str(args.source), "source_sha256": hashlib.sha256(args.source.read_bytes()).hexdigest(), "files": files}, indent=2), encoding="utf-8")
    print(f"Exported the approved mark to {assets} and {delivery}")


if __name__ == "__main__":
    main()
