"""Repack the painted watchtower and its two menu icons without rewriting UI/rules."""
from pathlib import Path
import re
from PIL import Image, PngImagePlugin
from fortification_art import frames
from wilderness_art import save

MOD = Path(__file__).resolve().parents[1]


def replace_icon(path, sprite, box, padding):
    source = Image.open(path)
    metadata = PngImagePlugin.PngInfo()
    for key, value in source.info.items():
        if isinstance(value, str):
            metadata.add_text(key, value)
    sheet = source.convert('RGBA')
    x, y, width, height = box
    picture = sprite.crop(sprite.getbbox())
    picture.thumbnail((width-padding, height-padding), Image.Resampling.LANCZOS)
    tile = Image.new('RGBA', (width, height))
    tile.alpha_composite(picture, ((width-picture.width)//2, (height-picture.height)//2))
    sheet.paste(tile, (x, y))
    sheet.save(path, pnginfo=metadata)


def main():
    poses = frames('woodtower')
    save('woodtower', poses)
    chrome = (MOD/'chrome.yaml').read_text(encoding='utf-8')
    region = re.search(r'^\t\twoodtower: (\d+), (\d+), (\d+), (\d+)$', chrome, re.M)
    assert region, 'Legacy watchtower icon region missing'
    replace_icon(MOD/'art/icons.png', poses[0], tuple(map(int, region.groups())), 2)

    # The catalog pads each category to 16 frames, each of 32x34 pixels.
    catalog = (MOD.parents[1]/'OpenRA.Mods.Medieval/ConstructionMenu.cs').read_text(encoding='utf-8')
    groups = re.findall(r'new\("(?!md\.)[^\n]+, \[\n(.*?)\]\)', catalog, re.S)
    matches = []
    for category, group in enumerate(groups):
        names = re.findall(r'new\("md\.([^\"]+)"', group)
        if 'woodtower' in names:
            matches.append(category*16 + names.index('woodtower'))
    assert len(matches) == 1, 'Expected one watchtower catalog entry'
    path = MOD/'art/construction-icons.png'
    source = Image.open(path)
    width, height = map(int, source.info['FrameSize'].split(','))
    columns = source.width//width
    index = matches[0]
    replace_icon(path, poses[0], ((index % columns)*width, (index//columns)*height, width, height), 0)


if __name__ == '__main__':
    main()
