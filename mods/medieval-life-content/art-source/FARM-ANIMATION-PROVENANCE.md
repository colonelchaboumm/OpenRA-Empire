# Ferme avec rotor séparé — 7 octobre 2026

Outil : générateur d’images intégré, via la compétence `imagegen` (pas de CLI ni de clé API).

Source locale conservée : `farm-animation-base.png`, transparent, 1280 × 1280. Sortie originale : `C:/Users/valen/.codex/generated_images/01a114d1-ec6d-75a1-8506-e817242f29ec/exec-5ffa9ca0-526f-451e-bf71-38178ac5dd3a.png`.

Référence : `analysis/medieval/farm-animation-reference.png`, première image du sprite antérieur de la ferme. Le nouveau bâtiment fixe est réduit à 184 × 184 par le compilateur ; un rotor natif à seize phases est dessiné autour du moyeu `(64, 82)`. La projection, l’ancrage au sol et l’emprise de neuf cases restent conservés. L’image générée n’a pas de pales fixes sous le rotor animé.

Prompt exact :

> Use case: precise-object-edit. Edit target: the attached existing isometric medieval game farm sprite. Create the SAME farm sprite but REMOVE ONLY the four large wooden windmill sails/blades so we can animate a separate rotor in game. Reconstruct the stone wall, small red roof and thatch behind the removed blades seamlessly. Keep a small round wooden axle/hub at the exact original center of the rotor, on the front face of the stone tower. Preserve the entire barn, thatched roofs, stone tower silhouette, windows, doors, sacks, fence, footprint, perspective, lighting, warm muted game palette, pixel-painted RTS art style and size relationships. No other building redesign. Preserve exact square canvas composition and placement: original building foundation sits around y=140 of 184, tower centered around x=64, original rotor hub approximately x=65,y=81 of the 184x184 source canvas. Output one isolated building with actual transparent alpha, generous existing transparent padding. No rotating sails at all, no added objects, no labels, no grid.
