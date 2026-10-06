#!/usr/bin/env bash
# Put the newest published APK into an ordinary ZIP and prove what is inside it, on a public host.
#
# Nothing is rebuilt, re-encoded or compressed: the ZIP is made from the bytes of the release asset
# itself, the APK is stored in it (not deflated), it is the only entry, and after upload the archive
# is downloaded back, unpacked and hashed against the file that was uploaded.
#
# The repository is private, so GitHub's own download needs a session; the link handed over must not.
#
# Run:  GITHUB_TOKEN=... bash tools/ci/relay_zip.sh
set -uo pipefail

REPO="${GITHUB_REPOSITORY:-sosisoochannel-cmd/Project-Aether}"
UA="aether-zip-relay/2.0 (+https://github.com/$REPO)"
API="https://api.github.com/repos/$REPO"

say() { echo "$@"; }
RESULTS=""
add() { RESULTS="${RESULTS}$1"$'\n'; say "  $1"; }
flush() { printf '::notice title=Relay results::%s\n' "$(printf '%s' "$RESULTS" | tr '\n' '|' | head -c 6000)"; }
short() { tr -d '\r\n' < "$1" 2>/dev/null | head -c "$2"; }
hdr() { grep -i "^$2" "$1" 2>/dev/null | head -1 | tr -d '\r\n' | sed "s/$2: //I"; }

# --- find the newest release that actually carries an APK ---------------------------------------
say "== newest release with an APK =="
release=$(curl -sS --max-time 120 -H "Authorization: Bearer ${GITHUB_TOKEN}" \
            -H "Accept: application/vnd.github+json" "$API/releases?per_page=20")
TAG=$(printf '%s' "$release" | jq -r '[.[] | select(any(.assets[]?; .name | endswith(".apk")))] | first | .tag_name // empty')
if [ -z "$TAG" ]; then
  printf '::error::no release in %s has an APK asset\n' "$REPO"
  exit 1
fi
ASSET=$(printf '%s' "$release" | jq -r --arg t "$TAG" \
          '[.[] | select(.tag_name == $t) | .assets[] | select(.name | endswith(".apk"))] | first | .name')
ASSET_ID=$(printf '%s' "$release" | jq -r --arg t "$TAG" \
          '[.[] | select(.tag_name == $t) | .assets[] | select(.name | endswith(".apk"))] | first | .id')
PUBLISHED_AT=$(printf '%s' "$release" | jq -r --arg t "$TAG" '[.[] | select(.tag_name == $t)] | first | .published_at')
add "release $TAG: $ASSET (asset $ASSET_ID, published $PUBLISHED_AT)"

# --- fetch it, exactly as published --------------------------------------------------------------
for attempt in 1 2 3; do
  curl -sSL --max-time 900 --retry 5 --retry-all-errors \
    -H "Authorization: Bearer ${GITHUB_TOKEN}" \
    -H "Accept: application/octet-stream" \
    -o "$ASSET" "$API/releases/assets/$ASSET_ID" && break
  say "download attempt $attempt failed; retrying"
  sleep 5
done
APK_SIZE=$(stat -c %s "$ASSET")
APK_SHA=$(sha256sum "$ASSET" | awk '{print $1}')
add "APK: $ASSET, $APK_SIZE bytes, sha256 $APK_SHA"

# --- the archive: one entry, the APK, stored -----------------------------------------------------
ZIPNAME="${ASSET%.apk}.zip"
python3 - "$ASSET" "$ZIPNAME" <<'PY'
import sys, zipfile
apk, zipname = sys.argv[1], sys.argv[2]
info = zipfile.ZipInfo(apk, (1980, 1, 1, 0, 0, 0))
info.compress_type = zipfile.ZIP_STORED          # the APK goes in untouched
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
ZIP_SIZE=$(stat -c %s "$ZIPNAME")
ZIP_SHA=$(sha256sum "$ZIPNAME" | awk '{print $1}')
add "ZIP: $ZIPNAME, $ZIP_SIZE bytes, sha256 $ZIP_SHA"

verify_zip() { # $1 zip path, $2 expected inner sha256; prints "name sha256" on success
  python3 - "$1" "$2" <<'PY'
import sys, zipfile, hashlib
path, want_sha = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(path) as z:
    names = [n for n in z.namelist()]
    if len(names) != 1 or not names[0].endswith(".apk"):
        print("ENTRIES %r" % (names,)); sys.exit(3)
    bad = z.testzip()
    if bad:
        print("CRC-BAD %s" % bad); sys.exit(5)
    h = hashlib.sha256()
    with z.open(names[0]) as f:
        while True:
            block = f.read(1 << 20)
            if not block:
                break
            h.update(block)
    got = h.hexdigest()
    if got != want_sha:
        print("SHA-MISMATCH %s" % got); sys.exit(4)
print("%s %s" % (names[0], got))
PY
}

local_out=$(verify_zip "$ZIPNAME" "$APK_SHA")
if [ $? -ne 0 ]; then
  add "LOCAL ZIP CHECK FAILED: $local_out"
  flush
  printf '::error::the ZIP does not hold the published APK\n'
  exit 1
fi
add "LOCAL ZIP CHECK ok: 1 entry, $local_out, CRC ok"

verified=0
: > links.txt

check() { # $1 label, $2 url
  local label="$1" url="$2" out="back-$1.zip" code got_size got_sha inner
  [ -n "$url" ] || { add "$label: no link"; return 1; }
  code=$(curl -sSL --max-time 1800 --retry 2 --retry-all-errors -A "$UA" -H 'Accept: */*' \
         -D "hdr-$1.txt" -o "$out" -w '%{http_code}' "$url" || echo 000)
  got_size=$(stat -c %s "$out" 2>/dev/null || echo 0)
  got_sha=$(sha256sum "$out" 2>/dev/null | awk '{print $1}')
  inner=$(verify_zip "$out" "$APK_SHA" 2>&1)
  if [ "$code" = 200 ] && [ "$got_size" = "$ZIP_SIZE" ] && [ "$got_sha" = "$ZIP_SHA" ] && [ "${inner##* }" = "$APK_SHA" ]; then
    add "$label: VERIFIED $url [zip ${got_size}B; inside: $inner; $(hdr "hdr-$1.txt" content-disposition)]"
    printf '%s|%s|%s|%s\n' "$label" "$url" "$got_size" "$inner" >> links.txt
    if [ "$verified" -lt 6 ]; then
      printf '::notice title=Verified %s::%s - %s bytes - contains %s - sha256 %s - downloaded back, unpacked, identical\n' \
        "$label" "$url" "$ZIP_SIZE" "${inner% *}" "$APK_SHA"
    fi
    verified=$((verified + 1))
    return 0
  fi
  add "$label: http=$code, ${got_size} bytes, zip-check=${inner} $(hdr "hdr-$1.txt" content-disposition)"
  return 1
}

# --- kappa.lol, which kept a name and served the bytes back every time ---------------------------
kcode=$(curl -sS --max-time 1800 -A "$UA" -F "file=@$ZIPNAME" -o kl.json -w '%{http_code}' \
        https://kappa.lol/api/upload || echo 000)
add "kappa upload: http=$kcode resp=$(short kl.json 240)"
klink=$(jq -r '.link // .url // empty' kl.json 2>/dev/null)
kid=$(jq -r '.id // empty' kl.json 2>/dev/null)
check kappa "$klink" || check kappa-id "https://kappa.lol/$kid" || true

# --- x0.at, the second host, so one outage is not a lost file ------------------------------------
xcode=$(curl -sS --max-time 1800 -A "$UA" -F "file=@$ZIPNAME" -o x0.txt -w '%{http_code}' https://x0.at || echo 000)
xurl=$(head -1 x0.txt | tr -d '\r\n')
add "x0.at upload: http=$xcode resp=$(short x0.txt 120)"
check x0.at "$xurl" || true

say
say "== verified links =="
cat links.txt || true
flush
if [ "$verified" -lt 1 ]; then
  printf '::error::no host returned the ZIP with the published APK inside it\n'
  exit 1
fi
printf '::notice title=Relay summary::%s verified ZIP link(s) for %s; the APK inside is %s bytes and hashes %s\n' \
  "$verified" "$TAG" "$APK_SIZE" "$APK_SHA"
