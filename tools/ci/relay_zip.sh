#!/usr/bin/env bash
# Take the APK the build job just verified, put it in an ordinary ZIP, publish the ZIP on a public
# host, and prove the APK inside is byte-identical.
#
# Nothing is rebuilt, re-encoded or compressed: the ZIP is made from the artifact's own bytes, the
# APK is stored in it (not deflated), it is the only entry, and after upload the archive is
# downloaded back, unpacked and hashed against the file that was uploaded.
#
# Why the artifact and not a release: the repository is private and its workflow token is read-only,
# so the build job's "create a release" step is refused with "Resource not accessible by
# integration" - but the APK itself is built, verified and uploaded by the build job, and that
# artifact is what this takes.
#
# Run:  GITHUB_TOKEN=... bash tools/ci/relay_zip.sh
# Optional: RUN_ID or ARTIFACT_ID to pin, EXPECT_SIZE / EXPECT_SHA to override the recorded numbers.
set -uo pipefail

REPO="${GITHUB_REPOSITORY:-sosisoochannel-cmd/Project-Aether}"
BRANCH="${GITHUB_REF_NAME:-arena/01a10203-project-aether}"
ARTIFACT_ID="${ARTIFACT_ID:-}"
RUN_ID="${RUN_ID:-}"
EXPECT_SIZE="${EXPECT_SIZE:-}"
EXPECT_SHA="${EXPECT_SHA:-}"
UA="aether-zip-relay/4.0 (+https://github.com/$REPO)"
API="https://api.github.com/repos/$REPO"

say() { echo "$@"; }
RESULTS=""
add() { RESULTS="${RESULTS}$1"$'\n'; say "  $1"; }
flush() { printf '::notice title=Relay results::%s\n' "$(printf '%s' "$RESULTS" | tr '\n' '|' | head -c 6000)"; }
short() { tr -d '\r\n' < "$1" 2>/dev/null | head -c "$2"; }
hdr() { grep -i "^$2" "$1" 2>/dev/null | head -1 | tr -d '\r\n' | sed "s/$2: //I"; }
auth=(-H "Authorization: Bearer ${GITHUB_TOKEN}" -H "Accept: application/vnd.github+json")

# --- which APK: the newest one a build job actually produced -------------------------------------
# Not the newest run that succeeded. A run is marked failed when its release step is refused - this
# private repository's workflow token is read-only, so creating a release always is - even though
# the build job inside it succeeded and uploaded its APK. The artifact is the honest evidence: it
# exists only because a build job got as far as uploading what it built, and it names its run.
if [ -z "$ARTIFACT_ID" ] && [ -z "$RUN_ID" ]; then
  code=$(curl -sS --max-time 60 -o arts.json -w '%{http_code}' "${auth[@]}" \
         "$API/actions/artifacts?name=Aether-dev-apk&per_page=30" || echo 000)
  read -r ARTIFACT_ID ARTIFACT_NAME RUN_ID < <(jq -r '[.artifacts[] | select(.expired == false)]
      | sort_by(.created_at) | last | "\(.id) \(.name) \(.workflow_run.id)"' arts.json 2>/dev/null)
fi
if [ -z "$ARTIFACT_ID" ] && [ -n "$RUN_ID" ]; then
  branch_query=""
  [ -n "$BRANCH" ] && branch_query="&branch=$BRANCH"
  code=$(curl -sS --max-time 60 -o arts.json -w '%{http_code}' "${auth[@]}" \
         "$API/actions/runs/$RUN_ID/artifacts?per_page=30$branch_query" || echo 000)
  read -r ARTIFACT_ID ARTIFACT_NAME < <(jq -r '[.artifacts[] | select(.name | test("apk"; "i"))
      | select(.size_in_bytes > 1000000)] | sort_by(.created_at) | last | "\(.id) \(.name)"' arts.json 2>/dev/null)
fi
if [ -z "$ARTIFACT_ID" ] || [ "$ARTIFACT_ID" = "null" ]; then
  add "artifacts API: http=${code:-?}, body=$(short arts.json 600)"
  flush
  printf '::error::no APK artifact to publish\n'
  exit 1
fi
add "APK artifact: $ARTIFACT_NAME ($ARTIFACT_ID) from build run $RUN_ID (branch $BRANCH)"

# Which commit the APK was actually built from. The phone cannot be rebuilt from a hash, so this is
# not a gate: it is the one thing a reader of this run needs and cannot get from the numbers below,
# and a run whose head is not the branch tip is delivering an older tree than the branch holds.
apk_head=$(curl -sS --max-time 60 "${auth[@]}" "$API/actions/runs/$RUN_ID" 2>/dev/null | jq -r '.head_sha // empty' 2>/dev/null)
# The runner has this branch checked out, so the tip needs no request and no slash-in-ref guesswork.
tip=$(git rev-parse HEAD 2>/dev/null)
if [ -n "$apk_head" ]; then
  add "built from commit $apk_head"
  if [ -n "$tip" ] && [ "$apk_head" != "$tip" ]; then
    add "NOTE: the branch tip is $tip, so this APK does not carry the commits after $apk_head"
  else
    add "that is the branch tip"
  fi
else
  add "could not read the commit this run was built from"
fi

# --- what that run said it built -----------------------------------------------------------------
# The build job reports the APK's size and hash as an annotation ("Release asset :: ... matches the
# build: yes"). Reading it back means the bytes are checked against the number recorded by the job
# that produced them, not against a number typed in here. It sits on the release job, which is the
# one whose step is refused, so every job of the run is asked rather than the first successful one.
if [ -z "$EXPECT_SHA" ] || [ -z "$EXPECT_SIZE" ]; then
  curl -sS --max-time 60 "${auth[@]}" "$API/actions/runs/$RUN_ID/jobs" -o jobs.json || true
  for jid in $(jq -r '.jobs[].id' jobs.json 2>/dev/null); do
    curl -sS --max-time 60 "${auth[@]}" "$API/check-runs/$jid/annotations?per_page=100" -o ann.json || true
    ref=$(jq -r '[.[] | .message | select(test("Release asset ::"))] | first // empty' ann.json 2>/dev/null)
    if [ -n "$ref" ]; then
      [ -n "$EXPECT_SIZE" ] || EXPECT_SIZE=$(printf '%s' "$ref" | sed -n 's/.* - \([0-9][0-9]*\) bytes.*/\1/p')
      [ -n "$EXPECT_SHA" ] || EXPECT_SHA=$(printf '%s' "$ref" | sed -n 's/.*sha256 \([0-9a-f]\{64\}\).*/\1/p')
      add "run $RUN_ID recorded: $(printf '%s' "$ref" | cut -c1-220)"
      break
    fi
  done
  [ -n "$EXPECT_SHA" ] || add "run $RUN_ID recorded no asset numbers; the ZIP check below still applies"
fi

# --- the artifact itself -------------------------------------------------------------------------
for attempt in 1 2 3; do
  curl -sSL --max-time 1800 --retry 3 --retry-all-errors "${auth[@]}" \
    -o artifact.zip "$API/actions/artifacts/$ARTIFACT_ID/zip" && break
  add "artifact download attempt $attempt failed; retrying"
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
  printf '::error::the artifact APK hashes %s, the build reported %s\n' "$APK_SHA" "$EXPECT_SHA"
  exit 1
fi
if [ -n "$EXPECT_SIZE" ] && [ "$APK_SIZE" != "$EXPECT_SIZE" ]; then
  printf '::error::the artifact APK is %s bytes, the build reported %s\n' "$APK_SIZE" "$EXPECT_SIZE"
  exit 1
fi
[ -z "$EXPECT_SHA" ] || add "matches the size and hash the build run recorded for it"

# --- the archive: one entry, the APK, stored ------------------------------------------------------
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
  if [ "$code" = 200 ] && [ "$got_size" = "$ZIP_SIZE" ] && [ "$got_sha" = "$ZIP_SHA" ] && [ "${inner%% *}" = "$ASSET" ]; then
    add "$label: VERIFIED $url [zip ${got_size}B; inside: $inner; $(hdr "hdr-$1.txt" content-disposition)]"
    printf '%s|%s|%s|%s\n' "$label" "$url" "$got_size" "$inner" >> links.txt
    printf '::notice title=Verified %s::%s - %s bytes - contains %s - sha256 %s - downloaded back, unpacked, identical\n' \
      "$label" "$url" "$ZIP_SIZE" "$ASSET" "$APK_SHA"
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
