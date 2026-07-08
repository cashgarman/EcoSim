# Asset credits

Wildlands EcoSim uses the following open-source art for zoomed-in creature sprites.

## Kenney Animal Pack Redux

- **Author:** Kenney (https://kenney.nl)
- **License:** [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)
- **Use:** Primary round-outline sprites for most species and 20 extra animals in `assets/creatures/extra_*.png`
- **Attribution:** Not required; crediting Kenney is appreciated.

Download: https://opengameart.org/content/animal-pack-redux  
Newer remastered pack (optional upgrade): https://kenney.nl/assets/animal-pack-remastered

## LPC Animals 2022 — Fox

- **Author:** bluecarrot16 (LPC Spring 2022 entry)
- **License:** CC0 for non-derivative sprites (fox woods sheet)
- **Use:** `fox.png` (first walk frame, padded)
- **Source:** https://opengameart.org/content/lpc-bears-deer-lions-and-more

## Kenney Tiny Creatures (optional reference)

- **Author:** Clint Bellanger (Kenney Tiny style)
- **License:** [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)
- **Use:** Referenced by import script for future tile-based additions
- **Source:** https://opengameart.org/content/tiny-creatures

## Species stand-ins

Some sim species reuse the closest Kenney silhouette where no exact match exists in the Redux pack:

| Species | Sprite source |
|---------|---------------|
| mouse | chick |
| deer | goat |
| elk | moose |
| beaver | duck |
| boar | pig |
| wolf | dog |
| hawk | parrot |

Replace these mappings in `assets/creatures/manifest.json` after importing Kenney Animal Pack Remastered or custom art.
