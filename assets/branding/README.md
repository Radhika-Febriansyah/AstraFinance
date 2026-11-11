# Astra Finance brand assets

Everything here is generated from one editable vector master. Don't edit the generated files by hand.

| Path | What it is |
|---|---|
| `source/astra-symbol.svg` | **Vector master** — the "Winged Star" symbol on a 100 × 100 grid. Edit the shape here. |
| `build-brand-assets.ps1` | Generator (WPF only, no third-party tools). Writes every file below. |
| `svg/` | Vector exports: symbol, horizontal and vertical lockups (light and dark), app icon, installer banner. |
| `png/` | The same assets as transparent PNGs at 512 and 1024 px (longest side). |
| `../../src/AstraFinance.App/Resources/astra.ico` | Windows icon: 16, 20, 24, 32, 40, 48, 64, 128 and 256 px. |
| `../../installer/branding/` | Inno Setup wizard bitmaps (24-bit BMP) for 100–250 % display scaling. |

Regenerate after changing the master (from the repository root):

```
powershell -NoProfile -ExecutionPolicy Bypass -STA -File assets\branding\build-brand-assets.ps1
```

The app's in-UI symbol (`Brand.Symbol` in `src/AstraFinance.App/Themes/Icons.xaml`) is the master path
scaled to a 24-unit grid (× 0.24). If you change the master, update that geometry too: the
`BrandAssetTests` unit test fails until both match.

## Palette

| Token | Hex | Use |
|---|---|---|
| Deep Navy | `#172554` | Dark surfaces, brand backgrounds, wordmark on light |
| Royal Blue | `#2563EB` | Primary interactions, highlights |
| White | `#FFFFFF` | Contrast, wordmark on dark |
| Sky (Royal Blue tint) | `#60A5FA` | End of the symbol gradient on Deep Navy only |

Symbol gradient, bottom-left to top-right: Deep Navy → Royal Blue on light backgrounds, Royal Blue → Sky on Deep Navy.
Flat 2D only: no bevels, 3D effects or heavy shadows.

## Usage

- **Light backgrounds:** `*-light` files (navy wordmark). **Dark or navy backgrounds:** `*-dark` files (white wordmark).
- **Clear space:** keep at least the width of the symbol's lower ray × 4 (about 10 % of the symbol height) free around the logo.
- **Minimum size:** symbol 16 px; horizontal lockup 120 px wide.
- Don't recolor, stretch, rotate, outline or add effects to the symbol, and don't rebuild the wordmark in another font.

## Wordmark font

The wordmark is "Astra Finance" set in **Segoe UI Variable Display Semibold** (normal spacing, Title Case), converted to
outlines in the exported files. Segoe UI ships with Windows under Microsoft's licence, which doesn't explicitly cover use as
a logo. If you need an unambiguous licence for the logo (for example, before registering it as a trademark), regenerate with an
open-licence font such as Inter (SIL Open Font License); install the font first, the generator refuses to fall back:

```
powershell -NoProfile -ExecutionPolicy Bypass -STA -File assets\branding\build-brand-assets.ps1 -WordmarkFont "Inter" -WordmarkWeight SemiBold
```

The app itself draws its text with the system's Segoe UI at run time.
