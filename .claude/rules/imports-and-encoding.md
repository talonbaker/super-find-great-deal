---
paths:
  - "**/*.import"
  - "assets/**"
  - ".gitattributes"
  - ".gitignore"
---

# Import sidecars, staging, and encoding — silent-corruption traps

- **`.import` churn is `core.autocrlf`, not content drift.** A wall of modified
  `.import` files after a Godot run has zero content diff. Never
  `git checkout -- '*.import'` to "clean up" — that CREATES the damage. And **never
  `git add -A` after running the test suite** — a suite pass regenerates ~141
  `.import` sidecars; stage files explicitly, always.
- **Texture `.import` traps:** `detect_3d/compress_to=1` silently re-imports a
  texture as VRAM-compressed on first 3D use (fatal for data masks);
  `fix_alpha_border=true` bleeds RGB into zero-alpha texels;
  `mipmaps/generate=false` makes any `filter_*_mipmap_*` hint a no-op and is the
  primary cause of ground shimmer. `assets/terrain/camp_splat.png.import` is the
  corrected reference.
- **Desktop compression only (s3tc/bptc), never `etc2_astc`** — `ART-BIBLE.md` §4.4.
- **Never round-trip a file through PowerShell `Get-Content`/`Set-Content`** — 5.1
  mangles UTF-8 (em-dashes, `§`). Use the harness Read/Write/Edit tools for file
  content, always.
