# Pazaryeri Entegrasyon

## PrimeviaSoft UI
- Styles come from `primeviasoft-brand` (brand.css / tailwind.preset.js / mui-theme.ts). Use the tokens; never hard-code hex values.
- Colors: navy `#13233F` (main, 30%), teal `#1F6F78` (links/accents), amber `#F2B33D` (only the single primary CTA per screen). Neutrals 60%.
- Text on amber is always navy. Never amber text on white.
- Fonts: Bricolage Grotesque for headings, IBM Plex Sans for everything else.
- Buttons and chips fully rounded; cards 22px radius, inputs 12px; no shadows, separate layers with `--line` borders.
- Dark theme: `data-theme="dark"` on `<html>`.
- Full guide only when needed: BRAND.md in primeviasoft-brand.
