#!/usr/bin/env bash
# Move the APK that is already published on the release to a host a phone can finish downloading
# from, and prove every copy byte-for-byte. This builds nothing and re-encodes nothing: one file is
# fetched from the release, uploaded as it is, downloaded back, and hashed.
#
# The hash it must match is the value the publish job recorded when it created the release, so the
# copy handed to a player is provably the same file the build produced.
#
# Run:  GITHUB_TOKEN=... bash tools/ci/relay_apk.sh
set -euo pipefail

REPO="${GITHUB_REPOSITORY:-sosisoochannel-cmd/Project-Aether}"
TAG="${TAG:-apk-6bf7101}"
ASSET="${ASSET:-Aether_0.1.0_6bf7101.apk}"
EXPECT_SIZE="${EXPECT_SIZE:-53287777}"
EXPECT_SHA="${EXPECT_SHA:-6724da9dacf09234d0a4d68cd310b2d0a4fa01f91fe90dd647470ce1d0ee9b64}"
UA="aether-apk-relay/1.0 (+https://github.com/$REPO)"
API="https://api.github.com/repos/$REPO"

say() { echo "$@"; }
notice() { echo "::notice title=$1::$2"; }

say "== source: $TAG / $ASSET =="
asset_id=$(curl -sS --max-time 120 \
  -H "Authorization: Bearer ${GITHUB_TOKEN}" \
  -H "Accept: application/vnd.github+json" \
  "$API/releases/tags/$TAG" \
  | jq -r --arg n "$ASSET" '.assets[] | select(.name == $n) | .id' | head -1)
if [ -z "$asset_id" ] || [ "$asset_id" = "null" ]; then
  echo "::error::release $TAG has no asset named $ASSET"
  exit 1
fi
say "asset id $asset_id"

for attempt in 1 2 3; do
  if curl -sSL --max-time 900 --retry 5 --retry-all-errors \
       -H "Authorization: Bearer ${GITHUB_TOKEN}" \
       -H "Accept: application/octet-stream" \
       -o "$ASSET" "$API/releases/assets/$asset_id"; then
    break
  fi
  say "download attempt $attempt failed; retrying"
  sleep 5
done

size=$(stat -c %s "$ASSET")
sha=$(sha256sum "$ASSET" | awk '{print $1}')
say "fetched: $size bytes, sha256 $sha"
if [ "$size" != "$EXPECT_SIZE" ] || [ "$sha" != "$EXPECT_SHA" ]; then
  echo "::error::the release asset is $size bytes / $sha, expected $EXPECT_SIZE / $EXPECT_SHA"
  exit 1
fi
notice "Source APK" "$ASSET - $size bytes - sha256 $sha - matches the published release"

verified=0
: > links.txt

check() { # $1 host label, $2 url
  local host="$1" url="$2" out="back-$1.bin"
  case "$url" in
    https://*) ;;
    *) say "  [$host] no usable link came back"; return 1 ;;
  esac
  if ! curl -sSL --max-time 900 --retry 3 --retry-all-errors -A "$UA" -o "$out" "$url"; then
    say "  [$host] download-back FAILED"
    return 1
  fi
  local got_size got_sha
  got_size=$(stat -c %s "$out")
  got_sha=$(sha256sum "$out" | awk '{print $1}')
  if [ "$got_size" = "$EXPECT_SIZE" ] && [ "$got_sha" = "$EXPECT_SHA" ]; then
    say "  [$host] VERIFIED  $url  ($got_size bytes, sha256 $got_sha)"
    notice "$host link" "$url - $got_size bytes - sha256 $got_sha - downloaded back and identical"
    printf '%s|%s|%s|%s\n' "$host" "$url" "$got_size" "$got_sha" >> links.txt
    verified=$((verified + 1))
    return 0
  fi
  say "  [$host] MISMATCH: came back as $got_size bytes, sha256 $got_sha"
  return 1
}

say "== pixeldrain =="
pd=$(curl -sS --max-time 900 -A "$UA" -T "$ASSET" "https://pixeldrain.com/api/file/$ASSET" || true)
pd_id=$(printf '%s' "$pd" | jq -r '.id // empty' 2>/dev/null || true)
if [ -n "$pd_id" ]; then
  check pixeldrain "https://pixeldrain.com/api/file/$pd_id?download" || true
else
  say "  [pixeldrain] upload rejected: $(printf '%s' "$pd" | head -c 200)"
fi

say "== catbox =="
cb=$(curl -sS --max-time 900 -A "$UA" \
  -F "reqtype=fileupload" -F "fileToUpload=@$ASSET" \
  https://catbox.moe/user/api.php || true)
check catbox "$cb" || true

say "== litterbox (72h) =="
lb=$(curl -sS --max-time 900 -A "$UA" \
  -F "reqtype=fileupload" -F "time=72h" -F "fileToUpload=@$ASSET" \
  https://litterbox.catbox.moe/resources/internals/api.php || true)
check litterbox "$lb" || true

say "== temp.sh =="
ts=$(curl -sS --max-time 900 -A "$UA" --upload-file "$ASSET" https://temp.sh/upload || true)
check temp.sh "$ts" || true

say "== oshi.at =="
oa=$(curl -sS --max-time 900 -A "$UA" --upload-file "$ASSET" https://oshi.at || true)
oshi_link=$(printf '%s\n' "$oa" | awk '/^DL:/{print $2; exit}')
check oshi.at "${oshi_link:-}" || true

say "== bashupload =="
bu=$(curl -sS --max-time 900 -A "$UA" --upload-file "$ASSET" https://bashupload.com/ || true)
bu_link=$(printf '%s\n' "$bu" | grep -oE 'https://bashupload\.com/[^[:space:]]+' | tail -1 || true)
check bashupload "${bu_link:-}" || true

for host in 0x0.st envs.sh; do
  say "== $host =="
  url=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" "https://$host" || true)
  check "${host%%.*}" "$url" || true
done

say "== filebin =="
bin="aether-6bf7101-$(date +%s)"
curl -sS --max-time 900 -A "$UA" -X POST -H "filename: $ASSET" \
  --data-binary "@$ASSET" "https://filebin.net/$bin" >/dev/null 2>&1 || true
check filebin "https://filebin.net/$bin/$ASSET" || true

say
say "== verified links =="
cat links.txt || true
if [ "$verified" -lt 1 ]; then
  echo "::error::no host returned a byte-identical copy of the APK"
  exit 1
fi
notice "Relay summary" "$verified host(s) verified: every link above was downloaded back and hashed $EXPECT_SHA"
