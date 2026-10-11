"""Small inventory symbol and UI wiring for the sheep -> cloth supply chain."""
from pathlib import Path
import re
from PIL import Image, ImageDraw
from wilderness_art import save

MOD = Path(__file__).resolve().parents[1]


def main():
    # A folded bolt, distinct from the existing sheep fleece and pitched tent.
    icon = Image.new('RGBA', (16, 16))
    d = ImageDraw.Draw(icon)
    d.polygon([(1, 6), (9, 2), (14, 5), (14, 11), (6, 15), (1, 12)], fill='#786649')
    d.polygon([(2, 6), (9, 3), (13, 5), (6, 9)], fill='#eee0bb')
    d.polygon([(2, 7), (6, 10), (6, 13), (2, 11)], fill='#bfa77b')
    d.polygon([(7, 10), (13, 7), (13, 10), (7, 13)], fill='#d8c595')
    d.line([(7, 11), (12, 9)], fill='#97815b')
    d.line([(5, 5), (10, 8), (10, 11)], fill='#826c50')
    save('cloth-icon', [icon])
    path = MOD / 'sequences/camping.yaml'
    text = path.read_text(encoding='utf-8')
    text = re.sub(r'\tcloth:\n(?:\t\t[^\n]*\n)*', '', text)
    text = text.replace('\twool:\n', '\twool:\n\tcloth:\n\t\tFilename: medieval|art/cloth-icon.png\n')
    path.write_text(text, encoding='utf-8', newline='\n')

    path = MOD / 'chrome/ingame-player.yaml'
    text = path.read_text(encoding='utf-8')
    if 'Label@MD_CAMP_CLOTH_TARGET:' not in text:
        start = text.index('\t\tBackground@MD_CAMP_PANEL:')
        end = text.index('\t\tBackground@', start + 10)
        block = text[start:end].replace('\t\t\tHeight: 382', '\t\t\tHeight: 422')
        block = re.sub(r'(\t{5}Y: )(\d+)', lambda m: m[1] + str(int(m[2]) + (40 if int(m[2]) >= 198 else 0)), block)
        extra = '\t\t\t\tLabel@MD_CAMP_CLOTH_TARGET:\n\t\t\t\t\tX: 14\n\t\t\t\t\tY: 194\n\t\t\t\t\tWidth: 430\n\t\t\t\t\tHeight: 30\n\t\t\t\t\tFont: TinyBold\n'
        for name, x in [('LESS', 458), ('MORE', 503)]:
            extra += f'\t\t\t\tButton@MD_CAMP_CLOTH_{name}:\n\t\t\t\t\tX: {x}\n\t\t\t\t\tY: 194\n\t\t\t\t\tWidth: 36\n\t\t\t\t\tHeight: 26\n\t\t\t\t\tFont: TinyBold\n\t\t\t\t\tBackground: md-button\n'
        text = text[:start] + block + extra + text[end:]
        path.write_text(text, encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
