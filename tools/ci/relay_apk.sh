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
cdisp() { grep -i '^content-disposition' "$1" 2>/dev/null | head -1 | tr -d '\r\n' | sed 's/content-disposition: //I'; }

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
  local host="$1" url="$2" limit="${3:-600}" ua="${CHECK_UA:-$UA}" out="back-$1.bin" code got_size got_sha
  if [ -z "$url" ] || [ "${url#http}" = "$url" ]; then
    add "$host: no link"
    return 1
  fi
  code=$(curl -sSL --max-time "$limit" --retry 2 --retry-all-errors -A "$ua" \
         -H 'Accept: */*' -D "hdr-$1.txt" -o "$out" -w '%{http_code}' "$url" || echo 000)
  got_size=$(stat -c %s "$out" 2>/dev/null || echo 0)
  got_sha=$(sha256sum "$out" 2>/dev/null | awk '{print $1}')
  if [ "$got_size" = "$EXPECT_SIZE" ] && [ "$got_sha" = "$EXPECT_SHA" ]; then
    add "$host: VERIFIED $url [$(ctype "hdr-$1.txt")|$(cdisp "hdr-$1.txt")]"
    printf '%s|%s|%s|%s\n' "$host" "$url" "$EXPECT_SIZE" "$EXPECT_SHA" >> links.txt
    if [ "$verified" -lt 8 ]; then
      printf '::notice title=Verified %s::%s - %s bytes - sha256 %s - downloaded back, identical\n' \
        "$host" "$url" "$EXPECT_SIZE" "$EXPECT_SHA"
    fi
    verified=$((verified + 1))
    return 0
  fi
  add "$host: http=$code, ${got_size} bytes, $(ctype "hdr-$1.txt") $(cdisp "hdr-$1.txt") head=$(short "$out" 50)"
  return 1
}

try_urls() { # $1 host label, then candidate urls
  local host="$1"; shift
  local u
  for u in "$@"; do
    [ -n "$u" ] || continue
    check "$host" "$u" 900 && return 0
  done
  return 1
}

links_in() { grep -oE 'https?://[A-Za-z0-9._~:/?#@!$&*+,;=%-]+' "$1" 2>/dev/null | sort -u | head -"${2:-3}"; }

# --- kappa.lol: it took the real .apk and served the exact bytes; name it, and name it .apk ----
kcode=$(curl -sS --max-time 600 -A "$UA" -F "file=@$ASSET" -o kl.json -w '%{http_code}' \
        https://kappa.lol/api/upload || echo 000)
add "kappa upload: http=$kcode resp=$(short kl.json 300)"
kurl=$(jq -r '.url // .data.url // empty' kl.json 2>/dev/null)
kid=$(jq -r '.id // .data.id // empty' kl.json 2>/dev/null)
if [ -n "$kurl" ]; then
  try_urls kappa "$kurl" || true
  case "$kurl" in
    *"$kid"*) try_urls kappa.apk "https://kappa.lol/$kid.apk" "$kurl.apk" || true ;;
  esac
fi

# --- tmpfiles.org: json api; the /dl/ shape is the direct one, and it wants a browser ----------
code=$(curl -sS --max-time 900 -A "$UA" -F "file=@$ASSET" -o tf.json -w '%{http_code}' \
       https://tmpfiles.org/api/v1/upload || echo 000)
add "tmpfiles upload: http=$code resp=$(short tf.json 140)"
tf_url=$(jq -r '.data.url // empty' tf.json 2>/dev/null)
tf_dl=$(printf '%s' "$tf_url" | sed 's|tmpfiles\.org/|tmpfiles.org/dl/|')
try_urls tmpfiles "$tf_dl" "$tf_url" || true
if [ -n "$tf_dl" ]; then
  CHECK_UA="$BUA" try_urls tmpfiles-br "$tf_dl" "$tf_url" || true
  CHECK_UA="$UA"
fi

# --- pixeldrain: anonymous PUT to a chosen id, which is the documented raw upload --------------
pd_id="aether6bf7101"
pcode=$(curl -sS --max-time 900 -A "$UA" -X PUT --data-binary "@$ASSET" \
        -H 'Content-Type: application/octet-stream' -o pd.json -w '%{http_code}' \
        "https://pixeldrain.com/api/file/$pd_id" || echo 000)
add "pixeldrain PUT: http=$pcode resp=$(short pd.json 160)"
if [ "$pcode" = 200 ] || [ "$pcode" = 201 ]; then
  try_urls pixeldrain "https://pixeldrain.com/api/file/$pd_id?download&name=$ASSET" \
    "https://pixeldrain.com/api/file/$pd_id" || true
fi

# --- litterbox: temporary catbox, 1 GB, its documented api params this time --------------------
lcode=$(curl -sS --max-time 900 -A "$UA" \
        -F "reqtype=fileupload" -F "time=72h" -F "fileToUpload=@$ASSET" \
        -o lb.txt -w '%{http_code}' \
        https://litterbox.catbox.moe/resources/internals/api.php || echo 000)
add "litterbox upload: http=$lcode resp=$(short lb.txt 160)"
lurl=$(grep -oE 'https://litter\.catbox\.moe/[A-Za-z0-9._-]+' lb.txt 2>/dev/null | head -1)
if [ -z "$lurl" ]; then
  lurl=$(head -1 lb.txt | tr -d '\r\n')
  case "$lurl" in *" ") lurl="" ;; esac
fi
try_urls litterbox "$lurl" || true

# --- uguu.se: temporary, 128 MB, its documented api --------------------------------------------
ucode=$(curl -sS --max-time 900 -A "$UA" -F "files[]=@$ASSET" -o ug.json -w '%{http_code}' \
        https://uguu.se/upload.php || echo 000)
add "uguu upload: http=$ucode resp=$(short ug.json 160)"
try_urls uguu $(jq -r '.files[0].url // empty' ug.json 2>/dev/null) \
  $(links_in ug.json 2 | grep -E '^https://[a-z]*\.?uguu\.se/') || true

# --- the .zip copy that x0.at already proved, kept so the run always reports one live link -----
cp "$ASSET" "$ZIPNAME"
code=$(curl -sS --max-time 600 -A "$UA" -F "file=@$ZIPNAME" -o z-x0.txt -w '%{http_code}' https://x0.at || echo 000)
zurl=$(head -1 z-x0.txt | tr -d '\r\n')
add "x0.at as .zip: http=$code resp=$(short z-x0.txt 90)"
if [ "${zurl#https://}" != "$zurl" ]; then try_urls x0.at-zip "$zurl" || true; fi

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
