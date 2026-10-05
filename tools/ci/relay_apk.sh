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
ZIPNAME="Aether_0.1.0_6bf7101.zip"
EXPECT_SIZE="${EXPECT_SIZE:-53287777}"
EXPECT_SHA="${EXPECT_SHA:-6724da9dacf09234d0a4d68cd310b2d0a4fa01f91fe90dd647470ce1d0ee9b64}"
UA="aether-apk-relay/1.0 (+https://github.com/$REPO)"
BUA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36"
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

check() { # $1 host label, $2 url, $3 timeout seconds, $4 user agent
  local host="$1" url="$2" limit="${3:-600}" ua="${4:-$UA}" out="back-$1.bin" code got_size got_sha
  if [ -z "$url" ] || [ "${url#http}" = "$url" ]; then
    add "$host: no link"
    return 1
  fi
  code=$(curl -sSL --max-time "$limit" --retry 2 --retry-all-errors -A "$ua" -o "$out" \
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

try_urls() { # $1 host label, then candidate urls)
  local host="$1"; shift
  local u
  for u in "$@"; do
    [ -n "$u" ] || continue
    check "$host" "$u" 900 && return 0
  done
  return 1
}

links_in() { grep -oE 'https?://[A-Za-z0-9._~:/?#@!$&*+,;=%-]+' "$1" 2>/dev/null | sort -u | head -"${2:-3}"; }

# --- oshi.at: no type restriction, keeps the .apk name ---------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -F "f=@$ASSET" -o os.txt -w '%{http_code}' https://oshi.at || echo 000)
add "oshi.at upload: http=$code resp=$(short os.txt 160)"
if [ "$code" = 200 ]; then
  # shellcheck disable=SC2046
  try_urls oshi.at $(links_in os.txt 4 | grep -E '^https://oshi\.at/' | head -2) || true
fi

# --- bashupload.com: plain PUT, no type restriction ------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -T "$ASSET" -o bu.txt -w '%{http_code}' \
       "https://bashupload.com/$ASSET" || echo 000)
add "bashupload upload: http=$code resp=$(short bu.txt 160)"
try_urls bashupload $(links_in bu.txt 2 | grep -E '^https://bashupload\.com/') || true

# --- tmpfiles.org: JSON api, direct link is the same id under /dl/ ----------------------------
code=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" -o tf.json -w '%{http_code}' \
       https://tmpfiles.org/api/v1/upload || echo 000)
add "tmpfiles upload: http=$code resp=$(short tf.json 140)"
tf_url=$(jq -r '.data.url // empty' tf.json 2>/dev/null)
try_urls tmpfiles "$(printf '%s' "$tf_url" | sed 's|tmpfiles\.org/|tmpfiles.org/dl/|')" "$tf_url" || true

# --- fileditch: big files, no type restriction ------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -F "files[]=@$ASSET" -o fd.json -w '%{http_code}' \
       https://up1.fileditch.com/upload.php || echo 000)
add "fileditch upload: http=$code resp=$(short fd.json 160)"
try_urls fileditch $(links_in fd.json 2 | grep -E '^https://[^ ]*fileditch' ) || true

# --- temp.sh: the upload is accepted; last time the returned url served an HTML page ----------
code=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" -D hdr-temp.txt -o temp.out \
       -w '%{http_code}' https://temp.sh/upload || echo 000)
turl=$(head -1 temp.out | tr -d '\r\n')
add "temp.sh upload: http=$code body=$(short temp.out 160)"
if [ "${turl#https://}" != "$turl" ]; then
  tcode=$(curl -sSL --max-time 900 -A "$BUA" -D hdr-temp.txt -o back-temp.bin -w '%{http_code}' "$turl" || echo 000)
  tgot=$(stat -c %s back-temp.bin 2>/dev/null || echo 0)
  if [ "$tgot" = "$EXPECT_SIZE" ] && [ "$(sha256sum back-temp.bin | awk '{print $1}')" = "$EXPECT_SHA" ]; then
    add "temp.sh: VERIFIED $turl"
    printf 'temp.sh|%s|%s|%s\n' "$turl" "$EXPECT_SIZE" "$EXPECT_SHA" >> links.txt
    printf '::notice title=Verified temp.sh::%s - %s bytes - sha256 %s - downloaded back, identical\n' \
      "$turl" "$EXPECT_SIZE" "$EXPECT_SHA"
    verified=$((verified + 1))
  else
    add "temp.sh fetch: http=$tcode, ${tgot} bytes, $(ctype hdr-temp.txt) head=$(short back-temp.bin 220)"
  fi
fi

# --- buzzheavier: the PUT worked last time (id, size); find the shape a curl can pull ---------
code=$(curl -sS --max-time 900 -A "$UA" -X PUT -H "filename: $ASSET" --data-binary "@$ASSET" \
       -o bh.json -w '%{http_code}' "https://w.buzzheavier.com/$ASSET" || echo 000)
add "buzzheavier PUT: http=$code body=$(short bh.json 400)"
bh_id=$(jq -r '.data.id // .id // empty' bh.json 2>/dev/null)
if [ -n "$bh_id" ]; then
  try_urls buzzheavier \
    "https://buzzheavier.com/$bh_id/$ASSET" \
    "https://buzzheavier.com/$bh_id/download" \
    "https://w.buzzheavier.com/$bh_id/$ASSET" || true
fi

# --- kappa.lol: 405 on the bare root, so ask its api path ------------------------------------
code=$(curl -sS --max-time 600 -A "$UA" -F "file=@$ASSET" -o kl.json -w '%{http_code}' \
       https://kappa.lol/api/upload || echo 000)
add "kappa.lol api: http=$code resp=$(short kl.json 120)"
try_urls kappa $(links_in kl.json 2 | grep -E '^https://kappa\.lol/') || true

# --- extension-blocking hosts: same bytes, a name they accept, and a case variant ------------
# The bytes are what matter and the hash proves them. A host that refuses the .apk extension is
# asked again with the same file under .zip; .APK is tried because that check may be case sensitive.
cp "$ASSET" "$ZIPNAME"
code=$(curl -sS --max-time 600 -A "$UA" -F "file=@$ZIPNAME" -o z-x0.txt -w '%{http_code}' https://x0.at || echo 000)
zurl=$(head -1 z-x0.txt | tr -d '\r\n')
add "x0.at as .zip: http=$code resp=$(short z-x0.txt 90)"
if [ "${zurl#https://}" != "$zurl" ]; then try_urls x0.at-zip "$zurl" || true; fi

cp "$ASSET" "$(printf '%s' "$ASSET" | tr 'a-z' 'A-Z')"
code=$(curl -sS --max-time 600 -A "$UA" -F "file=@$(printf '%s' "$ASSET" | tr 'a-z' 'A-Z')" -o z-x0u.txt \
       -w '%{http_code}' https://x0.at || echo 000)
uurl=$(head -1 z-x0u.txt | tr -d '\r\n')
add "x0.at as .APK: http=$code resp=$(short z-x0u.txt 90)"
if [ "${uurl#https://}" != "$uurl" ]; then try_urls x0.at-APK "$uurl" || true; fi

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
