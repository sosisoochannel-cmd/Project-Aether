#!/usr/bin/env bash
# Final check on the two published ZIP links, with the user-agent a phone actually sends.
#
# Nothing is uploaded here. Each link is fetched whole, hashed, unpacked, and the APK inside is
# hashed again; the response headers are recorded too, because content-length and accept-ranges are
# what decide whether a download that stalls can be resumed or must start over.
#
# Run:  bash tools/ci/verify_zip_links.sh
set -uo pipefail

ASSET="${ASSET:-Aether_0.1.0_6bf7101.apk}"
EXPECT_SHA="${EXPECT_SHA:-6724da9dacf09234d0a4d68cd310b2d0a4fa01f91fe90dd647470ce1d0ee9b64}"
EXPECT_ZIPSIZE="${EXPECT_ZIPSIZE:-53287923}"
EXPECT_ZIPSHA="${EXPECT_ZIPSHA:-8c716fb171c1702a621704fc8b00faf889b1dbdadc2513770af497b1b8a506a0}"
MUA="Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36"

RESULTS=""
add() { RESULTS="${RESULTS}$1"$'\n'; echo "  $1"; }
flush() {
  printf '::notice title=Relay results::%s\n' "$(printf '%s' "$RESULTS" | tr '\n' '|' | head -c 6000)"
}
hdr() { grep -i "^$2" "$1" 2>/dev/null | head -1 | tr -d '\r\n' | sed "s/$2: //I"; }

verify_zip() { # $1 zip path
  python3 - "$1" "$ASSET" "$EXPECT_SHA" <<'PY'
import sys, zipfile, hashlib
path, want_name, want_sha = sys.argv[1], sys.argv[2], sys.argv[3]
with zipfile.ZipFile(path) as z:
    names = z.namelist()
    if names != [want_name]:
        print("ENTRIES %r" % (names,)); sys.exit(3)
    bad = z.testzip()
    if bad:
        print("CRC-BAD %s" % bad); sys.exit(5)
    h = hashlib.sha256()
    with z.open(want_name) as f:
        while True:
            block = f.read(1 << 20)
            if not block:
                break
            h.update(block)
    got = h.hexdigest()
    if got != want_sha:
        print("SHA-MISMATCH %s" % got); sys.exit(4)
print("%s %s" % (want_name, got))
PY
}

verified=0
check() { # $1 label, $2 url
  local label="$1" url="$2" out="ph-$1.zip" code got_size got_sha inner
  code=$(curl -sSL --max-time 1800 --retry 2 --retry-all-errors -A "$MUA" \
         -H 'Accept: */*' -D "ph-$1.txt" -o "$out" -w '%{http_code}' "$url" || echo 000)
  got_size=$(stat -c %s "$out" 2>/dev/null || echo 0)
  got_sha=$(sha256sum "$out" 2>/dev/null | awk '{print $1}')
  inner=$(verify_zip "$out" 2>&1)
  if [ "$code" = 200 ] && [ "$got_size" = "$EXPECT_ZIPSIZE" ] && [ "$got_sha" = "$EXPECT_ZIPSHA" ] \
     && [ "${inner% *}" = "$ASSET" ] && [ "${inner##* }" = "$EXPECT_SHA" ]; then
    add "$label: PHONE-UA VERIFIED $url [${got_size}B; $(hdr "ph-$1.txt" content-length); $(hdr "ph-$1.txt" accept-ranges); $(hdr "ph-$1.txt" content-disposition); inside: $inner]"
    verified=$((verified + 1))
    return 0
  fi
  add "$label: FAILED $url [http=$code ${got_size}B inner=$inner $(hdr "ph-$1.txt" content-disposition)]"
  return 1
}

check x0 "https://x0.at/dx7R.zip" || true
check kappa-zip "https://kappa.lol/PdTWPN.zip" || true
check kappa-id "https://kappa.lol/PdTWPN" || true

flush
if [ "$verified" -lt 1 ]; then
  printf '::error::the published ZIP links did not verify with a phone user-agent\n'
  exit 1
fi
printf '::notice title=Relay summary::%s of 3 shapes verified with a phone user-agent; APK inside hashes %s\n' \
  "$verified" "$EXPECT_SHA"
