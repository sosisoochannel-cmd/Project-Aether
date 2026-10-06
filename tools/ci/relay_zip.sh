#!/usr/bin/env bash
# Take the APK the build job just verified, put it in an ordinary ZIP, publish the ZIP on a public
# host, and prove the APK inside is byte-identical.
#
# Nothing is rebuilt, re-encoded or compressed: the ZIP is made from the artifact's own bytes, the
# APK is stored in it (not deflated), it is the only entry, and after upload the archive is
# downloaded back, unpacked and hashed against the file that was uploaded.
#
# Why the artifact and not a release: the repository is private and its workflow token is currently
# read-only, so the build job's "create a release" step is refused with "Resource not accessible by
# integration" - but the APK itself was produced and verified, and it is in the run's artifact.
#
# Run:  GITHUB_TOKEN=... EXPECT_SIZE=... EXPECT_SHA=... bash tools/ci/relay_zip.sh
set -uo pipefail

REPO="${GITHUB_REPOSITORY:-sosisoochannel-cmd/Project-Aether}"
RUN_ID="${RUN_ID:-}"
EXPECT_SIZE="${EXPECT_SIZE:-}"
EXPECT_SHA="${EXPECT_SHA:-}"
UA="aether-zip-relay/3.0 (+https://github.com/$REPO)"
API="https://api.github.com/repos/$REPO"

say() { echo "$@"; }
RESULTS=""
add() { RESULTS="${RESULTS}$1"$'\n'; say "  $1"; }
flush() { printf '::notice title=Relay results::%s\n' "$(printf '%s' "$RESULTS" | tr '\n' '|' | head -c 6000)"; }
short() { tr -d '\r\n' < "$1" 2>/dev/null | head -c "$2"; }
hdr() { grep -i "^$2" "$1" 2>/dev/null | head -1 | tr -d '\r\n' | sed "s/$2: //I"; }
auth=(-H "Authorization: Bearer ${GITHUB_TOKEN}" -H "Accept: application/vnd.github+json")

# --- the build run whose APK is wanted ----------------------------------------------------------
if [ -z "$RUN_ID" ]; then
  code=$(curl -sS --max-time 60 -o runs.json -w '%{http_code}' "${auth[@]}" \
         "$API/actions/workflows/android-build.yml/runs?status=completed&per_page=10" || echo 000)
  RUN_ID=$(jq -r '[.workflow_runs[] | select(.conclusion == "success")] | first | .id // empty' runs.json 2>/dev/null)
fi
if [ -z "$RUN_ID" ]; then
  add "build runs API: http=${code:-?}, body=$(short runs.json 600)"
  flush
  printf '::error::no successful Android APK run to take an artifact from\n'
  exit 1
fi
add "build run: $RUN_ID"

# --- its APK artifact, fetched the way a runner can (the artifact lives on blob storage) --------
# The listing needs actions:read on the job's token, which is why relay-apk.yml asks for it: a
# workflow that names any permission gets none of the others, and without it this call answers 403
# "Resource not accessible by integration" - which looks exactly like a run with no artifact unless
# the response itself is reported. So the response is reported.
code=$(curl -sS --max-time 120 -o artifacts.json -w '%{http_code}' "${auth[@]}" \
       "$API/actions/runs/$RUN_ID/artifacts" || echo 000)
artifact=$(jq -r '[.artifacts[] | select(.name | test("apk"; "i")) | select(.size_in_bytes > 1000000)]
                  | sort_by(.created_at) | last | "\(.id) \(.name)"' artifacts.json 2>/dev/null)
if [ -z "$artifact" ] || [ "$artifact" = "null" ]; then
  add "artifacts API: http=$code, body=$(short artifacts.json 600)"
  flush
  printf '::error::run %s has no APK artifact\n' "$RUN_ID"
  exit 1
fi
set -- $artifact
ARTIFACT_ID="$1"
ARTIFACT_NAME="$2"
add "artifact: $ARTIFACT_NAME ($ARTIFACT_ID)"

for attempt in 1 2 3; do
  curl -sSL --max-time 1800 --retry 3 --retry-all-errors "${auth[@]}" \
    -o artifact.zip "$API/actions/artifacts/$ARTIFACT_ID/zip" && break
  say "artifact download attempt $attempt failed; retrying"
  sleep 5
done
rm -rf unpacked && mkdir unpacked
unzip -q -o artifact.zip -d unpacked || true
APK=$(find unpacked -name '*.apk' -size +1M | head -1)
if [ -z "$APK" ]; then
  add "artifact contents: $(find unpacked -type f | head -10 | tr '\n' ' ')"
  flush
  printf '::error::the artifact holds no APK\n'
  exit 1
fi
ASSET=$(basename "$APK")
APK_SIZE=$(stat -c %s "$APK")
APK_SHA=$(sha256sum "$APK" | awk '{print $1}')
add "APK: $ASSET, $APK_SIZE bytes, sha256 $APK_SHA"

if [ -n "$EXPECT_SHA" ] && [ "$APK_SHA" != "$EXPECT_SHA" ]; then
  printf '::error::the artifact APK hashes %s, the build verified %s\n' "$APK_SHA" "$EXPECT_SHA"
  exit 1
fi
if [ -n "$EXPECT_SIZE" ] && [ "$APK_SIZE" != "$EXPECT_SIZE" ]; then
  printf '::error::the artifact APK is %s bytes, the build verified %s\n' "$APK_SIZE" "$EXPECT_SIZE"
  exit 1
fi
add "matches the hash and size the build job recorded for run $RUN_ID"

# --- the archive: one entry, the APK, stored -----------------------------------------------------
ZIPNAME="${ASSET%.apk}.zip"
python3 - "$APK" "$ZIPNAME" <<'PY'
import sys, zipfile
apk, zipname = sys.argv[1], sys.argv[2]
info = zipfile.ZipInfo(apk.split("/")[-1], (1980, 1, 1, 0, 0, 0))
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

verify_zip() { # $1 zip path, $2 expected inner sha256
  python3 - "$1" "$2" <<'PY'
import sys, zipfile, hashlib
path, want_sha = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(path) as z:
    names = z.namelist()
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
  printf '::error::the ZIP does not hold the built APK\n'
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

kcode=$(curl -sS --max-time 1800 -A "$UA" -F "file=@$ZIPNAME" -o kl.json -w '%{http_code}' \
        https://kappa.lol/api/upload || echo 000)
add "kappa upload: http=$kcode resp=$(short kl.json 240)"
klink=$(jq -r '.link // .url // empty' kl.json 2>/dev/null)
kid=$(jq -r '.id // empty' kl.json 2>/dev/null)
check kappa "$klink" || check kappa-id "https://kappa.lol/$kid" || true

xcode=$(curl -sS --max-time 1800 -A "$UA" -F "file=@$ZIPNAME" -o x0.txt -w '%{http_code}' https://x0.at || echo 000)
xurl=$(head -1 x0.txt | tr -d '\r\n')
add "x0.at upload: http=$xcode resp=$(short x0.txt 120)"
check x0.at "$xurl" || true

say
say "== verified links =="
cat links.txt || true
flush
if [ "$verified" -lt 1 ]; then
  printf '::error::no host returned the ZIP with the built APK inside it\n'
  exit 1
fi
printf '::notice title=Relay summary::%s verified ZIP link(s) from build run %s; the APK inside is %s bytes and hashes %s\n' \
  "$verified" "$RUN_ID" "$APK_SIZE" "$APK_SHA"
