#!/usr/bin/env bash
# Move the APK that is already published on the release to a public host a phone can finish
# downloading from, and prove every copy byte-for-byte. This builds nothing and re-encodes nothing:
# the file is fetched from the release, uploaded as it is, downloaded back, and hashed against the
# value the publish job recorded.
#
# The repository is private, so nothing about GitHub is downloadable without a session - which is
# why this exists at all. Every outcome is collected into one workflow notice at the end, because
# GitHub shows only ten annotations per step and the job log cannot be read from the environment
# this is driven from.
#
# Run:  GITHUB_TOKEN=... bash tools/ci/relay_apk.sh
set -uo pipefail

REPO="${GITHUB_REPOSITORY:-sosisoochannel-cmd/Project-Aether}"
TAG="${TAG:-apk-6bf7101}"
ASSET="${ASSET:-Aether_0.1.0_6bf7101.apk}"
EXPECT_SIZE="${EXPECT_SIZE:-53287777}"
EXPECT_SHA="${EXPECT_SHA:-6724da9dacf09234d0a4d68cd310b2d0a4fa01f91fe90dd647470ce1d0ee9b64}"
UA="aether-apk-relay/1.0 (+https://github.com/$REPO)"
API="https://api.github.com/repos/$REPO"

say() { echo "$@"; }
RESULTS=""
add() { RESULTS="${RESULTS}$1"$'\n'; say "  $1"; }
flush() {
  local body
  body=$(printf '%s' "$RESULTS" | tr '\n' '|' | head -c 6000)
  printf '::notice title=Relay results::%s\n' "$body"
}
short() { tr -d '\r\n' < "$1" 2>/dev/null | head -c "$2"; }
ctype() { grep -i '^content-type' "$1" 2>/dev/null | head -1 | tr -d '\r\n' | sed 's/content-type: //I'; }

say "== source: $TAG / $ASSET =="
asset_id=$(curl -sS --max-time 120 \
  -H "Authorization: Bearer ${GITHUB_TOKEN}" \
  -H "Accept: application/vnd.github+json" \
  "$API/releases/tags/$TAG" \
  | jq -r --arg n "$ASSET" '.assets[] | select(.name == $n) | .id' | head -1)
if [ -z "$asset_id" ] || [ "$asset_id" = "null" ]; then
  printf '::error::release %s has no asset named %s\n' "$TAG" "$ASSET"
  exit 1
fi

for attempt in 1 2 3; do
  curl -sSL --max-time 900 --retry 5 --retry-all-errors \
    -H "Authorization: Bearer ${GITHUB_TOKEN}" \
    -H "Accept: application/octet-stream" \
    -o "$ASSET" "$API/releases/assets/$asset_id" && break
  say "download attempt $attempt failed; retrying"
  sleep 5
done

size=$(stat -c %s "$ASSET")
sha=$(sha256sum "$ASSET" | awk '{print $1}')
if [ "$size" != "$EXPECT_SIZE" ] || [ "$sha" != "$EXPECT_SHA" ]; then
  printf '::error::the release asset is %s bytes / %s, expected %s / %s\n' \
    "$size" "$sha" "$EXPECT_SIZE" "$EXPECT_SHA"
  exit 1
fi
add "SOURCE ok: $size bytes, sha256 $sha (matches the release)"

verified=0
: > links.txt

check() { # $1 host label, $2 url, $3 timeout seconds
  local host="$1" url="$2" limit="${3:-600}" out="back-$1.bin" code got_size got_sha
  if [ -z "$url" ] || [ "${url#http}" = "$url" ]; then
    add "$host: no link"
    return 1
  fi
  code=$(curl -sSL --max-time "$limit" --retry 2 --retry-all-errors -A "$UA" -o "$out" \
         -w '%{http_code}' "$url" || echo 000)
  got_size=$(stat -c %s "$out" 2>/dev/null || echo 0)
  got_sha=$(sha256sum "$out" 2>/dev/null | awk '{print $1}')
  if [ "$got_size" = "$EXPECT_SIZE" ] && [ "$got_sha" = "$EXPECT_SHA" ]; then
    add "$host: VERIFIED $url"
    printf '%s|%s|%s|%s\n' "$host" "$url" "$EXPECT_SIZE" "$EXPECT_SHA" >> links.txt
    if [ "$verified" -lt 8 ]; then
      printf '::notice title=Verified %s::%s - %s bytes - sha256 %s - downloaded back, identical\n' \
        "$host" "$url" "$EXPECT_SIZE" "$EXPECT_SHA"
    fi
    verified=$((verified + 1))
    return 0
  fi
  add "$host: http=$code, ${got_size} bytes, $(ctype "hdr-$1.txt") head=$(short "$out" 70)"
  return 1
}

# --- temp.sh --------------------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" -D hdr-temp.txt -o temp.out \
       -w '%{http_code}' https://temp.sh/upload || echo 000)
url=$(head -1 temp.out | tr -d '\r\n')
add "temp.sh upload: http=$code body=$(short temp.out 160)"
if [ "${url#https://}" != "$url" ]; then
  code=$(curl -sSL --max-time 900 -A "$UA" -D hdr-temp.txt -o back-temp.bin -w '%{http_code}' \
         "$url" || echo 000)
  got=$(stat -c %s back-temp.bin 2>/dev/null || echo 0)
  if [ "$got" = "$EXPECT_SIZE" ] && [ "$(sha256sum back-temp.bin | awk '{print $1}')" = "$EXPECT_SHA" ]; then
    add "temp.sh: VERIFIED $url"
    printf 'temp.sh|%s|%s|%s\n' "$url" "$EXPECT_SIZE" "$EXPECT_SHA" >> links.txt
    printf '::notice title=Verified temp.sh::%s - %s bytes - sha256 %s - downloaded back, identical\n' \
      "$url" "$EXPECT_SIZE" "$EXPECT_SHA"
    verified=$((verified + 1))
  else
    add "temp.sh: http=$code, ${got} bytes, $(ctype hdr-temp.txt) head=$(short back-temp.bin 70)"
  fi
fi

# --- gofile ---------------------------------------------------------------------------------
srv=$(curl -sS --max-time 60 https://api.gofile.io/servers | jq -r '.data.servers[0].name // empty' 2>/dev/null)
if [ -n "$srv" ]; then
  code=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" -o gf.json -w '%{http_code}' \
         "https://$srv.gofile.io/contents/uploadfile" || echo 000)
  add "gofile upload: srv=$srv http=$code keys=$(jq -r '.data | keys | join(",")' gf.json 2>/dev/null) page=$(jq -r '.data.downloadPage // "?"' gf.json 2>/dev/null)"
  fid=$(jq -r '.data.id // empty' gf.json 2>/dev/null)
  folder=$(jq -r '.data.parentFolder // empty' gf.json 2>/dev/null)
  for shape in "download/$folder/$fid/$ASSET" "download/$fid/$ASSET" "download/web/$fid/$ASSET"; do
    [ -n "$fid" ] && { check gofile "https://$srv.gofile.io/$shape" 900 && break; }
  done
else
  add "gofile: no server answered"
fi

# --- buzzheavier ----------------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -X PUT -H "filename: $ASSET" --data-binary "@$ASSET" \
       -o bh.json -w '%{http_code}' "https://w.buzzheavier.com/$ASSET" || echo 000)
add "buzzheavier PUT: http=$code body=$(short bh.json 140)"
bh_id=$(jq -r '.data.id // .id // empty' bh.json 2>/dev/null)
if [ -n "$bh_id" ]; then check buzzheavier "https://buzzheavier.com/$bh_id" 900 || true; fi

# --- filebin --------------------------------------------------------------------------------
bin="aether-6bf7101-$(date +%s)"
code=$(curl -sS --max-time 900 -A "$UA" -X POST -H "filename: $ASSET" \
       -H "Content-Type: application/octet-stream" --data-binary "@$ASSET" \
       -o fb.txt -w '%{http_code}' "https://filebin.net/$bin/$ASSET" || echo 000)
add "filebin POST: http=$code body=$(short fb.txt 140)"
if [ "$code" = 201 ] || [ "$code" = 200 ]; then
  check filebin "https://filebin.net/$bin/$ASSET" 900 || true
fi

# --- hosts that refuse the .apk extension: try the same bytes under a .zip name --------------
# The bytes are what matter and the hash proves them; a host that blocks the extension is asked
# again with a name it accepts, and the file only needs renaming back on the phone.
cp "$ASSET" "Aether_0.1.0_6bf7101.zip"
for host in x0.at qu.ax; do
  code=$(curl -sS --max-time 600 -A "$UA" -F "file=@Aether_0.1.0_6bf7101.zip" -o "z-$host.txt" \
         -w '%{http_code}' "https://$host" || echo 000)
  url=$(head -1 "z-$host.txt" | tr -d '\r\n')
  add "$host as .zip: http=$code resp=$(short "z-$host.txt" 90)"
  if [ "${url#https://}" != "$url" ]; then check "$host-zip" "$url" 900 || true; fi
done

# --- pixeldrain, once more, with a form POST -------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" -o pd.json -w '%{http_code}' \
       https://pixeldrain.com/api/file || echo 000)
add "pixeldrain POST: http=$code body=$(short pd.json 90)"
pd_id=$(jq -r '.id // empty' pd.json 2>/dev/null)
if [ -n "$pd_id" ]; then check pixeldrain "https://pixeldrain.com/api/file/$pd_id?download" 900 || true; fi

say
say "== verified links =="
cat links.txt || true
flush
if [ "$verified" -lt 1 ]; then
  printf '::error::no host returned a byte-identical copy of the APK\n'
  exit 1
fi
printf '::notice title=Relay summary::%s verified link(s); each was downloaded back and hashed %s\n' \
  "$verified" "$EXPECT_SHA"
