"""Draw the original PPAE expression-folder icon. Requires Pillow; no third-party artwork."""
# Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
from pathlib import Path
from PIL import Image, ImageDraw


def create_icon(destination):
    scale = 4
    image = Image.new("RGBA", (256 * scale, 256 * scale))
    draw = ImageDraw.Draw(image)
    white = (255, 255, 255, 255)
    face = (35, 46, 65, 255)

    def box(bounds):
        return tuple(value * scale for value in bounds)

    draw.rounded_rectangle(box((24, 40, 116, 106)), radius=16 * scale, fill=white)
    draw.rounded_rectangle(box((24, 72, 232, 216)), radius=20 * scale, fill=white)
    draw.ellipse(box((79, 121, 95, 137)), fill=face)
    draw.ellipse(box((161, 121, 177, 137)), fill=face)
    draw.arc(box((84, 132, 172, 184)), 10, 170, fill=face, width=11 * scale)
    image = image.resize((256, 256), Image.Resampling.LANCZOS)
    destination.parent.mkdir(parents=True, exist_ok=True)
    image.save(destination)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("destination", type=Path)
    create_icon(parser.parse_args().destination)
