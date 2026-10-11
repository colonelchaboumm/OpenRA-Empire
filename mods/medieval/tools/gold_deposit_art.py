"""Pack image-generated gold outcrop masters into the existing native resource frames.

Only alpha cropping, uniform downscaling and frame registration are performed
here. Artistic changes belong in the masters, made with the image-generation tool.
"""
from PIL import Image
from wilderness_art import MOD, save


def pack(master, bounds, bottom):
    image = Image.open(MOD / 'art-source' / master).convert('RGBA')
    alpha = image.getchannel('A')
    if alpha.getextrema()[0] == 255:
        raise ValueError(f'{master}: a transparent master is required')
    box = alpha.point(lambda a: 255 if a > 12 else 0).getbbox()
    if box is None:
        raise ValueError(f'{master}: empty master')
    image = image.crop(box)
    image.thumbnail(bounds, Image.Resampling.LANCZOS)
    frame = Image.new('RGBA', (96, 96))
    frame.alpha_composite(image, ((96 - image.width) // 2, bottom - image.height))
    return frame


def main():
    # Sprite centre + sequence offset (-12) = ground origin y60. The lower
    # silhouette reaches y70, the native mining renderer's fixed y+10 anchor.
    save('gold', [pack('gold-deposit-isometric.png', (72, 54), 70)])
    save('gold-rubble', [pack('gold-deposit-exhausted.png', (62, 24), 71)])
    print('Packed gold and exhausted rubble: 96x96 RGBA, registered ground anchor.')


if __name__ == '__main__':
    main()
