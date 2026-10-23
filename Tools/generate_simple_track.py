"""Generate the single-lane PNG from the same dimensions used by the Unity map."""
import re
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ROOT / 'Assets/Scripts/StadionTracking'


def constant(source, name):
    return float(re.search(r'\b' + name + r'\s*=\s*([\d.]+)', source).group(1))


# Generate the single-lane PNG from the shared geometry and map dimensions.
def main():
    geometry = (SCRIPTS / 'StadiumTrackGeometry.cs').read_text()
    mapping = (SCRIPTS / 'SimpleTrackMap.cs').read_text()
    width = int(constant(mapping, 'PixelWidth'))
    height = int(constant(mapping, 'PixelHeight'))
    scale = constant(mapping, 'PixelsPerMetre')
    straight = constant(geometry, 'StraightLength')
    radius = constant(geometry, 'InnerRadius') + 0.30
    h = straight / 2

    supersampling = 4
    image = Image.new('RGB', (width * supersampling, height * supersampling), '#F7F9FC')
    draw = ImageDraw.Draw(image)
    def capsule(offset, color):
        r = radius * scale + offset
        bounds = ((width / 2 - h * scale - r) * supersampling,
                  (height / 2 - r) * supersampling,
                  (width / 2 + h * scale + r) * supersampling,
                  (height / 2 + r) * supersampling)
        draw.rounded_rectangle(bounds, radius=r * supersampling, fill=color)

    capsule(27, '#B9D8DF')
    capsule(22, '#17677A')
    capsule(-22, '#B9D8DF')
    capsule(-27, '#F7F9FC')
    x = (width / 2 + h * scale) * supersampling
    y = (height / 2 + radius * scale) * supersampling
    draw.line([(x, y - 23 * supersampling), (x, y + 23 * supersampling)],
              fill='#FFFFFF', width=5 * supersampling)
    image = image.resize((width, height), Image.Resampling.LANCZOS)
    output = SCRIPTS / 'Resources/SimpleTrack.png'
    image.save(output)
    print(output)


if __name__ == '__main__':
    main()
