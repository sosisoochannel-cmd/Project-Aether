#!/usr/bin/env bash
# Move the APK that is already published on the release to a host a phone can finish downloading
# from, and prove every copy byte-for-byte. This builds nothing and re-encodes nothing: one file is
# fetched from the release, uploaded as it is, downloaded back, and hashed.
#
# The hash it must match is the value the publish job recorded when it created the release, so the
# copy handed to a player is provably the same file the build produced.
#
# Every host reports one line as a workflow notice, because the job log itself cannot be read from
# the environment this is driven from - only the annotations can.
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
report() { printf '::notice title=%s::%s\n' "$1" "${2//$'\n'/ }"; }
# Short form of a response, for a notice line: no newlines, no huge bodies.
head_response() { tr -d '\r\n' < "$1" 2>/dev/null | head -c 130; }

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
say "fetched: $size bytes, sha256 $sha"
if [ "$size" != "$EXPECT_SIZE" ] || [ "$sha" != "$EXPECT_SHA" ]; then
  printf '::error::the release asset is %s bytes / %s, expected %s / %s\n' \
    "$size" "$sha" "$EXPECT_SIZE" "$EXPECT_SHA"
  exit 1
fi
report "Source APK" "$ASSET - $size bytes - sha256 $sha - matches the published release"

verified=0
: > links.txt

check() { # $1 host label, $2 url
  local host="$1" url="$2" out="back-$1.bin" code got_size got_sha
  if [ -z "$url" ] || [ "${url#https://}" = "$url" ]; then
    report "$host" "no link came back"
    return 1
  fi
  code=$(curl -sSL --max-time 900 --retry 3 --retry-all-errors -A "$UA" -o "$out" \
         -w '%{http_code}' "$url" || echo 000)
  got_size=$(stat -c %s "$out" 2>/dev/null || echo 0)
  got_sha=$(sha256sum "$out" 2>/dev/null | awk '{print $1}')
  if [ "$got_size" = "$EXPECT_SIZE" ] && [ "$got_sha" = "$EXPECT_SHA" ]; then
    report "$host" "VERIFIED $url - $got_size bytes - sha256 $got_sha - downloaded back, identical"
    printf '%s|%s|%s|%s\n' "$host" "$url" "$got_size" "$got_sha" >> links.txt
    verified=$((verified + 1))
    return 0
  fi
  report "$host" "MISMATCH http=$code - got ${got_size} bytes, sha ${got_sha:0:16}... from $url"
  return 1
}

# --- pixeldrain -----------------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -T "$ASSET" -o pd.json -w '%{http_code}' \
       "https://pixeldrain.com/api/file/$ASSET" || echo 000)
pd_id=$(jq -r '.id // empty' pd.json 2>/dev/null)
if [ -n "$pd_id" ]; then
  check pixeldrain "https://pixeldrain.com/api/file/$pd_id?download" || true
else
  report pixeldrain "upload http=$code - $(head_response pd.json)"
fi

# --- catbox (permanent) ---------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -F "reqtype=fileupload" -F "fileToUpload=@$ASSET" \
       -o cb.txt -w '%{http_code}' https://catbox.moe/user/api.php || echo 000)
cb_url=$(grep -oE 'https://files\.catbox\.moe/[^[:space:]]+' cb.txt | head -1 || true)
if [ -n "$cb_url" ]; then check catbox "$cb_url" || true
else report catbox "upload http=$code - $(head_response cb.txt)"; fi

# --- litterbox (72h) ------------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -F "reqtype=fileupload" -F "time=72h" \
       -F "fileToUpload=@$ASSET" -o lb.txt -w '%{http_code}' \
       https://litterbox.catbox.moe/resources/internals/api.php || echo 000)
lb_url=$(grep -oE 'https://litterbox\.catbox\.moe/[^[:space:]]+' lb.txt | head -1 || true)
if [ -n "$lb_url" ]; then check litterbox "$lb_url" || true
else report litterbox "upload http=$code - $(head_response lb.txt)"; fi

# --- temp.sh --------------------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" --upload-file "$ASSET" -o ts.txt -w '%{http_code}' \
       https://temp.sh/upload || echo 000)
ts_url=$(grep -oE 'https://temp\.sh/[^[:space:]]+' ts.txt | head -1 || true)
if [ -n "$ts_url" ]; then check temp.sh "$ts_url" || true
else report temp.sh "upload http=$code - $(head_response ts.txt)"; fi

# --- oshi.at --------------------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" --upload-file "$ASSET" -o oa.txt -w '%{http_code}' \
       https://oshi.at || echo 000)
oa_url=$(awk '/^DL:/{print $2; exit}' oa.txt || true)
if [ -n "$oa_url" ]; then check oshi.at "$oa_url" || true
else report oshi.at "upload http=$code - $(head_response oa.txt)"; fi

# --- bashupload -----------------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" --upload-file "$ASSET" -o bu.txt -w '%{http_code}' \
       https://bashupload.com/ || echo 000)
bu_url=$(grep -oE 'https://bashupload\.com/[^[:space:]]+' bu.txt | tail -1 || true)
if [ -n "$bu_url" ]; then check bashupload "$bu_url" || true
else report bashupload "upload http=$code - $(head_response bu.txt)"; fi

# --- 0x0 clones -----------------------------------------------------------------------------
for host in 0x0.st envs.sh ttm.sh; do
  code=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" -o "x-$host.txt" \
         -w '%{http_code}' "https://$host" || echo 000)
  url=$(head -1 "x-$host.txt" | tr -d '\r\n')
  if [ -n "$url" ] && [ "${url#https://}" != "$url" ]; then
    check "${host%%.*}" "$url" || true
  else
    report "$host" "upload http=$code - $(head_response "x-$host.txt")"
  fi
done

# --- filebin --------------------------------------------------------------------------------
bin="aether-6bf7101-$(date +%s)"
code=$(curl -sS --max-time 900 -A "$UA" -X POST -H "filename: $ASSET" \
       --data-binary "@$ASSET" -o fb.txt -w '%{http_code}' "https://filebin.net/$bin" || echo 000)
if [ "$code" = "201" ] || [ "$code" = "200" ] || [ "$code" = "409" ]; then
  check filebin "https://filebin.net/$bin/$ASSET" || true
else
  report filebin "upload http=$code - $(head_response fb.txt)"
fi

# --- transfer.sh clone ----------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" --upload-file "$ASSET" -o tr.txt -w '%{http_code}' \
       "https://transfer.archivete.am/$ASSET" || echo 000)
tr_url=$(grep -oE 'https://transfer\.archivete\.am/[^[:space:]]+' tr.txt | tail -1 || true)
if [ -n "$tr_url" ]; then check transfer "$tr_url" || true
else report transfer "upload http=$code - $(head_response tr.txt)"; fi

# --- pomf clone -----------------------------------------------------------------------------
code=$(curl -sS --max-time 900 -A "$UA" -F "files[]=@$ASSET" -o pf.json -w '%{http_code}' \
       https://pomf.lain.la/upload.php || echo 000)
pf_url=$(jq -r '.files[0].url // empty' pf.json 2>/dev/null)
if [ -n "$pf_url" ]; then check pomf "$pf_url" || true
else report pomf "upload http=$code - $(head_response pf.json)"; fi

say
say "== verified links =="
cat links.txt || true
if [ "$verified" -lt 1 ]; then
  printf '::error::no host returned a byte-identical copy of the APK\n'
  exit 1
fi
printf '::notice title=Relay summary::%s host(s) verified; each was downloaded back and hashed %s\n' \
  "$verified" "$EXPECT_SHA"
