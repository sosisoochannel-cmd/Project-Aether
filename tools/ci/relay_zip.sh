#!/usr/bin/env bash
# Publish the APK that is already on the release as an ordinary ZIP, and prove what is inside it.
#
# Nothing is rebuilt: the ZIP is made from the very bytes of the published asset. The APK is stored
# in the archive as it is (no compression, no re-encoding, no other file alongside it), the archive
# is uploaded whole, then downloaded back, unpacked, and the inner file is hashed against the value
# the publish job recorded.
#
# The phone finishes a ZIP download from these hosts but not a bare .apk one, so the link handed
# over is the ZIP's.
#
# Run:  GITHUB_TOKEN=... bash tools/ci/relay_zip.sh
set -uo pipefail

REPO="${GITHUB_REPOSITORY:-sosisoochannel-cmd/Project-Aether}"
TAG="${TAG:-apk-6bf7101}"
ASSET="${ASSET:-Aether_0.1.0_6bf7101.apk}"
ZIPNAME="${ZIPNAME:-Aether_0.1.0_6bf7101.zip}"
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
hdr() { grep -i "^$2" "$1" 2>/dev/null | head -1 | tr -d '\r\n' | sed "s/$2: //I"; }

# --- fetch the published APK, and check it is the file the publish job verified ------------------
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

# --- make the ZIP: one entry, the APK, stored, nothing else --------------------------------------
python3 - "$ASSET" "$ZIPNAME" <<'PY'
import sys, zipfile
apk, zipname = sys.argv[1], sys.argv[2]
info = zipfile.ZipInfo(apk, (1980, 1, 1, 0, 0, 0))
info.compress_type = zipfile.ZIP_STORED      # the APK goes in untouched, not compressed
info.create_system = 3
info.external_attr = 0o644 << 16
with zipfile.ZipFile(zipname, 'w', allowZip64=True) as z:
    with open(apk, 'rb') as src, z.open(info, 'w') as dst:
        while True:
            block = src.read(1 << 20)
            if not block:
                break
            dst.write(block)
PY
zsize=$(stat -c %s "$ZIPNAME")
zsha=$(sha256sum "$ZIPNAME" | awk '{print $1}')
add "ZIP built: $ZIPNAME, $zsize bytes, sha256 $zsha"

# --- verify what is inside it, locally, before anything is uploaded -------------------------------
verify_zip() { # $1 zip path, prints "<entry> <sha256>" or an error, exit 0 only when exact
  python3 - "$1" "$ASSET" "$EXPECT_SHA" <<'PY'
import sys, zipfile, hashlib
path, want_name, want_sha = sys.argv[1], sys.argv[2], sys.argv[3]
with zipfile.ZipFile(path) as z:
    names = z.namelist()
    if names != [want_name]:
        print("ENTRIES %r" % (names,))
        sys.exit(3)
    bad = z.testzip()
    if bad:
        print("CRC-BAD %s" % bad)
        sys.exit(5)
    h = hashlib.sha256()
    with z.open(want_name) as f:
        while True:
            block = f.read(1 << 20)
            if not block:
                break
            h.update(block)
    got = h.hexdigest()
    if got != want_sha:
        print("SHA-MISMATCH %s" % got)
        sys.exit(4)
print("%s %s" % (want_name, got))
PY
}

local_out=$(verify_zip "$ZIPNAME")
if [ $? -ne 0 ]; then
  add "LOCAL ZIP CHECK FAILED: $local_out"
  flush
  printf '::error::the ZIP does not hold the expected APK\n'
  exit 1
fi
add "LOCAL ZIP CHECK ok: 1 entry, $local_out, CRC ok"

verified=0
: > links.txt

check() { # $1 host label, $2 url
  local host="$1" url="$2" out="back-$1.zip" code got_size got_sha inner
  if [ -z "$url" ] || [ "${url#http}" = "$url" ]; then
    add "$host: no link"
    return 1
  fi
  code=$(curl -sSL --max-time 1800 --retry 2 --retry-all-errors -A "$UA" \
         -H 'Accept: */*' -D "hdr-$1.txt" -o "$out" -w '%{http_code}' "$url" || echo 000)
  got_size=$(stat -c %s "$out" 2>/dev/null || echo 0)
  got_sha=$(sha256sum "$out" 2>/dev/null | awk '{print $1}')
  inner=$(verify_zip "$out" 2>&1)
  if [ "$code" = 200 ] && [ "$got_size" = "$zsize" ] && [ "$got_sha" = "$zsha" ] && [ "${inner% *}" = "$ASSET" ]; then
    add "$host: VERIFIED $url [zip ${got_size}B sha256 ${got_sha:0:12}…; inside: $inner; $(hdr "hdr-$1.txt" content-disposition)]"
    printf '%s|%s|%s|%s|%s\n' "$host" "$url" "$got_size" "$inner" "$EXPECT_SHA" >> links.txt
    if [ "$verified" -lt 8 ]; then
      printf '::notice title=Verified %s::%s - %s bytes - contains %s - sha256 %s - downloaded back, unpacked, identical\n' \
        "$host" "$url" "$got_size" "$ASSET" "$EXPECT_SHA"
    fi
    verified=$((verified + 1))
    return 0
  fi
  add "$host: http=$code, ${got_size} bytes, zip-check=${inner} $(hdr "hdr-$1.txt" content-disposition)"
  return 1
}

try_urls() { local host="$1"; shift; local u; for u in "$@"; do [ -n "$u" ] || continue; check "$host" "$u" && return 0; done; return 1; }

# --- kappa.lol: it kept the APK's own name and served it back byte-for-byte ----------------------
kcode=$(curl -sS --max-time 1800 -A "$UA" -F "file=@$ZIPNAME" -o kl.json -w '%{http_code}' \
        https://kappa.lol/api/upload || echo 000)
add "kappa upload: http=$kcode resp=$(short kl.json 300)"
klink=$(jq -r '.link // .url // empty' kl.json 2>/dev/null)
kid=$(jq -r '.id // empty' kl.json 2>/dev/null)
try_urls kappa "$klink" "https://kappa.lol/$kid.zip" "https://kappa.lol/$kid" || true

# --- x0.at: it took a 53 MB zip before, and answers with the url -------------------------------
xcode=$(curl -sS --max-time 1800 -A "$UA" -F "file=@$ZIPNAME" -o x0.txt -w '%{http_code}' https://x0.at || echo 000)
xurl=$(head -1 x0.txt | tr -d '\r\n')
add "x0.at upload: http=$xcode resp=$(short x0.txt 120)"
try_urls x0.at "$xurl" || true

say
say "== verified zip links =="
cat links.txt || true
flush
if [ "$verified" -lt 1 ]; then
  printf '::error::no host returned the ZIP with the expected APK inside it\n'
  exit 1
fi
printf '::notice title=Relay summary::%s verified ZIP link(s); the APK inside hashes %s\n' \
  "$verified" "$EXPECT_SHA"
