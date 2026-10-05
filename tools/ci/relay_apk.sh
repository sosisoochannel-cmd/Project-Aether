#!/usr/bin/env bash
# Move the APK that is already published on the release to hosts a phone can finish downloading
# from, and prove every copy byte-for-byte. This builds nothing and re-encodes nothing: the file is
# fetched from the release, uploaded as it is, downloaded back, and hashed against the value the
# publish job recorded.
#
# Every outcome is collected into one workflow notice at the end, because GitHub shows only ten
# annotations per step and the job log itself cannot be read from the environment this is driven
# from.
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
CORE="https://github.com/$REPO/releases/download/$TAG/$ASSET"

say() { echo "$@"; }
RESULTS=""
add() { RESULTS="${RESULTS}$1"$'\n'; say "  $1"; }
flush() { # one compact notice with everything that happened
  # A workflow command ends at the first newline, so the lines are joined before they are emitted -
  # otherwise only the first result would ever be visible.
  local body
  body=$(printf '%s' "$RESULTS" | tr '\n' '|' | head -c 6000)
  printf '::notice title=Relay results::%s\n' "$body"
}
short() { tr -d '\r\n' < "$1" 2>/dev/null | head -c "$2"; }

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
    add "$host: no link came back"
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
  add "$host: http=$code, got ${got_size} bytes / sha ${got_sha:0:12}..."
  return 1
}

# --- upload hosts ---------------------------------------------------------------------------
code=$(curl -sS --max-time 300 -A "$UA" -F "file=@$ASSET" -o temp.json -w '%{http_code}' \
       https://temp.sh/upload || echo 000)
url=$(head -1 temp.json | tr -d '\r\n')
if [ "${url#https://}" != "$url" ]; then check temp.sh "$url" 900 || true
else add "temp.sh: upload http=$code - $(short temp.json 100)"; fi

bin="aether-6bf7101-$(date +%s)"
code=$(curl -sS --max-time 600 -A "$UA" -X POST -H "filename: $ASSET" \
       --data-binary "@$ASSET" -o fb.txt -w '%{http_code}' "https://filebin.net/$bin" || echo 000)
if [ "$code" = 201 ] || [ "$code" = 200 ] || [ "$code" = 409 ]; then
  check filebin "https://filebin.net/$bin/$ASSET" 900 || true
else add "filebin: upload http=$code - $(short fb.txt 100)"; fi

code=$(curl -sS --max-time 600 -A "$UA" -F "files[]=@$ASSET" -o pf.json -w '%{http_code}' \
       https://pomf.lain.la/upload.php || echo 000)
url=$(jq -r '.files[0].url // empty' pf.json 2>/dev/null)
if [ -n "$url" ]; then check pomf "$url" 900 || true
else add "pomf.lain.la: upload http=$code - $(short pf.json 100)"; fi

for host in qu.ax cockfile.com; do
  code=$(curl -sS --max-time 600 -A "$UA" -F "files[]=@$ASSET" -o "p-$host.json" -w '%{http_code}' \
         "https://$host/upload.php" || echo 000)
  url=$(jq -r '.files[0].url // empty' "p-$host.json" 2>/dev/null)
  if [ -n "$url" ]; then check "$host" "$url" 900 || true
  else add "$host: upload http=$code - $(short "p-$host.json" 100)"; fi
done

for host in x0.at envs.sh; do
  code=$(curl -sS --max-time 600 -A "$UA" -F "file=@$ASSET" -o "x-$host.txt" -w '%{http_code}' \
         "https://$host" || echo 000)
  url=$(head -1 "x-$host.txt" | tr -d '\r\n')
  if [ "${url#https://}" != "$url" ]; then check "$host" "$url" 900 || true
  else add "$host: upload http=$code - $(short "x-$host.txt" 120)"; fi
done

code=$(curl -sS --max-time 600 -A "$UA" -H "filename: $ASSET" --data-binary "@$ASSET" \
       -o bh.json -w '%{http_code}' "https://w.buzzheavier.com/$ASSET" || echo 000)
bh_url=$(jq -r '.data.id // empty' bh.json 2>/dev/null)
if [ -n "$bh_url" ]; then check buzzheavier "https://buzzheavier.com/$bh_url" 900 || true
else add "buzzheavier: upload http=$code - $(short bh.json 100)"; fi

code=$(curl -sS --max-time 600 -A "$UA" -F "files[]=@$ASSET" -o ka.json -w '%{http_code}' \
       https://kappa.lol/upload.php || echo 000)
ka_url=$(jq -r '.files[0].url // empty' ka.json 2>/dev/null)
if [ -n "$ka_url" ]; then check kappa "$ka_url" 900 || true
else add "kappa.lol: upload http=$code - $(short ka.json 100)"; fi

code=$(curl -sS --max-time 900 -A "$UA" --upload-file "$ASSET" -o tr.txt -w '%{http_code}' \
       "https://transfer.archivete.am/$ASSET" || echo 000)
tr_url=$(grep -oE 'https://transfer\.archivete\.am/[^[:space:]]+' tr.txt | tail -1 || true)
if [ -n "$tr_url" ]; then check transfer.archivete.am "$tr_url" 900 || true
else add "transfer.archivete.am: upload http=$code - $(short tr.txt 100)"; fi

code=$(curl -sS --max-time 600 -A "$UA" -F "files[]=@$ASSET" -o fd.json -w '%{http_code}' \
       https://up1.fileditch.com/upload.php || echo 000)
url=$(jq -r '.files[0].url // empty' fd.json 2>/dev/null)
if [ -n "$url" ]; then check fileditch "$url" 900 || true
else add "fileditch: upload http=$code - $(short fd.json 100)"; fi

srv=$(curl -sS --max-time 60 https://api.gofile.io/servers | jq -r '.data.servers[0].name // empty' 2>/dev/null)
if [ -n "$srv" ]; then
  code=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" -o gf.json -w '%{http_code}' \
         "https://$srv.gofile.io/contents/uploadfile" || echo 000)
  fid=$(jq -r '.data.id // empty' gf.json 2>/dev/null)
  if [ -n "$fid" ]; then
    check gofile "https://$srv.gofile.io/download/direct/$fid/$ASSET" 900 || true
  else
    add "gofile: upload http=$code - $(short gf.json 100)"
  fi
else
  add "gofile: no server answered"
fi

code=$(curl -sS --max-time 300 -A "$UA" -T "$ASSET" -o pd.json -w '%{http_code}' \
       "https://pixeldrain.com/api/file/$ASSET" || echo 000)
pd_id=$(jq -r '.id // empty' pd.json 2>/dev/null)
if [ -n "$pd_id" ]; then check pixeldrain "https://pixeldrain.com/api/file/$pd_id?download" 900 || true
else add "pixeldrain: upload http=$code - $(short pd.json 90)"; fi

# --- public mirrors of the release itself ---------------------------------------------------
# No upload: these stream the published asset from GitHub through another host, which is exactly
# what is wanted if the problem is the phone's path to GitHub's CDN. Each is verified by hashing
# what the mirror serves.
for prefix in https://ghproxy.net/ https://gh-proxy.com/ https://ghfast.top/ \
              https://hub.gitmirror.com/ https://gh.llkk.cc/ https://mirror.ghproxy.com/ \
              https://ghproxy.cc/ https://ghps.cc/; do
  name="mirror:${prefix#https://}"
  name="${name%/}"
  check "$name" "$prefix$CORE" 900 || true
done

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
