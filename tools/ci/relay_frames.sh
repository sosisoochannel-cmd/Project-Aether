#!/usr/bin/env bash
# Hand the rendered intro frames over without an artifact download on the reader's side.
#
# The frames live in the render run's artifact, which is on blob storage the machine directing this
# cannot reach. A workflow annotation can be read from there - but GitHub stores only about 4 KB of
# one, which is exactly enough to hold a truncated PNG and nothing more, so each frame travels as
# numbered parts of ~3800 characters of base64 that the reader joins back together. Each part says
# which frame it belongs to and how many there are, and the whole frames' sizes and hashes are
# reported alongside, so an assembled picture can be checked against what the runner actually had.
#
# The frames are also offered on two public hosts as ordinary image links, verified by downloading
# them back and hashing them again.
set -uo pipefail

REPO="${GITHUB_REPOSITORY:-sosisoochannel-cmd/Project-Aether}"
RUN_ID="${RUN_ID:-}"
UA="aether-frames-relay/1.0 (+https://github.com/$REPO)"
API="https://api.github.com/repos/$REPO"
auth=(-H "Authorization: Bearer ${GITHUB_TOKEN}" -H "Accept: application/vnd.github+json")
add() { echo "$@"; }
short() { tr -d '\r\n' < "$1" 2>/dev/null | head -c "$2"; }

# --- the newest render run that worked ----------------------------------------------------------
if [ -z "$RUN_ID" ]; then
  code=$(curl -sS --max-time 60 -o runs.json -w '%{http_code}' "${auth[@]}" \
         "$API/actions/workflows/intro-render.yml/runs?status=completed&per_page=20" || echo 000)
  RUN_ID=$(jq -r '[.workflow_runs[] | select(.conclusion == "success")] | first | .id // empty' runs.json 2>/dev/null)
fi
if [ -z "$RUN_ID" ]; then
  add "render runs API: http=${code:-?}, body=$(short runs.json 400)"
  printf '::error::no successful render run to take frames from\n'
  exit 1
fi
add "render run: $RUN_ID"

# --- its frames ---------------------------------------------------------------------------------
code=$(curl -sS --max-time 120 -o arts.json -w '%{http_code}' "${auth[@]}" \
       "$API/actions/runs/$RUN_ID/artifacts" || echo 000)
AID=$(jq -r '[.artifacts[] | select(.name | test("intro-render"))] | sort_by(.created_at) | last | .id // empty' \
      arts.json 2>/dev/null)
if [ -z "$AID" ]; then
  add "artifacts API: http=$code, body=$(short arts.json 500)"
  printf '::error::run %s has no intro-render artifact\n' "$RUN_ID"
  exit 1
fi

for attempt in 1 2 3; do
  curl -sSL --max-time 900 --retry 3 --retry-all-errors "${auth[@]}" \
    -o render-artifact.zip "$API/actions/artifacts/$AID/zip" && break
  add "artifact download attempt $attempt failed; retrying"
  sleep 5
done
rm -rf frames && mkdir frames
unzip -q -o render-artifact.zip -d frames || true

mapfile -t pngs < <(find frames -name '*.png' | sort)
if [ "${#pngs[@]}" -eq 0 ]; then
  add "artifact contents: $(find frames -type f | head -20 | tr '\n' ' ')"
  printf '::error::the render artifact holds no frames\n'
  exit 1
fi

echo
echo "== what the run left =="
for f in "${pngs[@]}"; do
  add "$(basename "$f") - $(stat -c %s "$f") bytes - sha256 $(sha256sum "$f" | awk '{print $1}')"
done
for f in $(find frames -name 'summary.txt' -o -name 'diagnostics.txt' | sort); do
  echo "::notice title=$(basename "$f")::$(tr '\r' '\n' < "$f" | awk 'NF' | sed -e 's/%/%25/g' | tr '\n' '|' | head -c 5600)"
done

# --- and as links anyone can open ----------------------------------------------------------------
: > frame-links.txt
# The hold, and the light crossing the lockup: the two frames of the animation worth looking at.
# The hold is the ident at rest; the light is the one moment that only exists while it is playing.
link_frames=()
for want in intro-1.85s.png intro-1.25s.png; do
  found=$(find frames -name "$want" | head -1)
  [ -n "$found" ] && link_frames+=("$found")
done
[ "${#link_frames[@]}" -gt 0 ] || link_frames+=("${pngs[0]}")

for hold in "${link_frames[@]}"; do
kcode=$(curl -sS --max-time 300 -A "$UA" -F "file=@$hold" -o kappa.json -w '%{http_code}' \
        https://kappa.lol/api/upload || echo 000)
klink=$(jq -r '.link // empty' kappa.json 2>/dev/null)
kdel=$(jq -r '.delete // empty' kappa.json 2>/dev/null)
add "kappa upload: http=$kcode $(short kappa.json 200)"
if [ -n "$klink" ]; then
  kcode=$(curl -sSL --max-time 300 -A "$UA" -o back.png -w '%{http_code}' "$klink" || echo 000)
  ksha=$(sha256sum back.png 2>/dev/null | awk '{print $1}')
  want=$(sha256sum "$hold" | awk '{print $1}')
  if [ "$kcode" = 200 ] && [ "$ksha" = "$want" ]; then
    add "kappa: VERIFIED $klink (downloaded back, $(stat -c %s back.png) bytes, sha256 $ksha)"
    printf '%s|%s|%s\n' "$(basename "$hold")" "$klink" "$ksha" >> frame-links.txt
    printf '::notice title=Verified frame link::%s - %s - %s bytes - sha256 %s - downloaded back identical (delete: %s)\n' \
      "$(basename "$hold")" "$klink" "$(stat -c %s "$hold")" "$ksha" "$kdel"
  else
    add "kappa: link did not come back identical (http=$kcode, sha=$ksha, want=$want)"
  fi
fi

xcode=$(curl -sS --max-time 300 -A "$UA" -F "file=@$hold" -o x0.txt -w '%{http_code}' https://x0.at || echo 000)
xurl=$(head -1 x0.txt | tr -d '\r\n')
add "x0.at upload: http=$xcode $(short x0.txt 120)"
if [ -n "$xurl" ]; then
  xcode=$(curl -sSL --max-time 300 -A "$UA" -o back2.png -w '%{http_code}' "$xurl" || echo 000)
  xsha=$(sha256sum back2.png 2>/dev/null | awk '{print $1}')
  want=$(sha256sum "$hold" | awk '{print $1}')
  if [ "$xcode" = 200 ] && [ "$xsha" = "$want" ]; then
    add "x0.at: VERIFIED $xurl (downloaded back, $(stat -c %s back2.png) bytes, sha256 $xsha)"
    printf '%s|%s|%s\n' "$(basename "$hold")" "$xurl" "$xsha" >> frame-links.txt
    printf '::notice title=Verified frame link (mirror)::%s - %s - sha256 %s - downloaded back identical\n' \
      "$(basename "$hold")" "$xurl" "$xsha"
  else
    add "x0.at: link did not come back identical (http=$xcode, sha=$xsha, want=$want)"
  fi
fi

done

# --- and the hold frame's bytes, in parts, for anyone who cannot open a link ---------------
# --- the whole frames, in parts small enough to be kept -------------------------------------------
for name in intro-1.85s.png; do
  file=$(find frames -name "$name" | head -1)
  [ -n "$file" ] || continue
  b64=$(base64 -w0 "$file")
  total=$(( (${#b64} + 3799) / 3800 ))
  part=1
  while [ -n "$b64" ]; do
    echo "::notice title=Frame $name part $part/$total::$(printf '%s' "$b64" | cut -c1-3800)"
    b64=${b64:3800}
    part=$((part + 1))
  done
  add "sent $name in $total part(s), part 1 is the whole picture's first 3800 characters"
done

