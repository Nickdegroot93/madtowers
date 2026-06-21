#!/usr/bin/env bash
#
# Compress a chapter background or level thumbnail to the project's standard: downscale the long
# edge and re-encode as JPG (opaque art only). macOS `sips` — no extra tooling. See images.md.
#
#   Tools/compress_chapter_image.sh background "src.png" "Assets/Art/Chapters/<Chapter>/<chapter>.jpg"
#   Tools/compress_chapter_image.sh thumbnail  "src.png" "Assets/Art/Chapters/<Chapter>/<chapter>-1.jpg"
#
# background -> long edge <= 1440 px, JPG q82  (full-screen, cover-fit)
# thumbnail  -> long edge <=  800 px, JPG q80  (small card art)
#
# Only downscales (never upscales past the source). The output folder lives under
# Assets/Art/Chapters/, where MenuArtImportSettings auto-imports it as a UI sprite.
set -euo pipefail

kind="${1:?usage: compress_chapter_image.sh background|thumbnail <in> <out>}"
src="${2:?missing input image}"
dst="${3:?missing output path}"

case "$kind" in
  background) max=1440; quality=82 ;;
  thumbnail)  max=800;  quality=80 ;;
  *) echo "first arg must be 'background' or 'thumbnail'" >&2; exit 1 ;;
esac

w=$(sips -g pixelWidth  "$src" | awk '/pixelWidth/{print $2}')
h=$(sips -g pixelHeight "$src" | awk '/pixelHeight/{print $2}')
long=$(( w > h ? w : h ))

if [ "$long" -gt "$max" ]; then
  sips -Z "$max" -s format jpeg -s formatOptions "$quality" "$src" --out "$dst" >/dev/null
else
  # already within budget — convert format only, no upscaling
  sips -s format jpeg -s formatOptions "$quality" "$src" --out "$dst" >/dev/null
fi

before=$(du -h "$src" | cut -f1)
after=$(du -h "$dst" | cut -f1)
dims=$(sips -g pixelWidth -g pixelHeight "$dst" | awk '/pixelWidth|pixelHeight/{print $2}' | paste -sd'x' -)
echo "$kind: $before -> $after  ($dims)  $dst"
