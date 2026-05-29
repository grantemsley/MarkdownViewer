#!/usr/bin/env bash
# Sample bash script — exercises the text viewer + bash syntax highlight.
# Pretends to deploy a static site to an S3-flavoured bucket.

set -euo pipefail

BUCKET="${BUCKET:-my-site}"
DIST_DIR="${DIST_DIR:-./dist}"
CACHE_TTL="${CACHE_TTL:-3600}"

log() {
  printf "[%s] %s\n" "$(date -u +%FT%TZ)" "$*"
}

require() {
  command -v "$1" >/dev/null 2>&1 || {
    log "missing required command: $1"
    exit 1
  }
}

require aws
require gzip

if [[ ! -d "$DIST_DIR" ]]; then
  log "no $DIST_DIR/ — run 'npm run build' first"
  exit 1
fi

log "syncing $DIST_DIR -> s3://$BUCKET"

aws s3 sync "$DIST_DIR" "s3://$BUCKET" \
  --delete \
  --cache-control "public, max-age=$CACHE_TTL" \
  --exclude "*.html" \
  --exclude "*.map"

aws s3 sync "$DIST_DIR" "s3://$BUCKET" \
  --exclude "*" \
  --include "*.html" \
  --cache-control "public, max-age=60, must-revalidate" \
  --content-type "text/html; charset=utf-8"

log "invalidating CDN"
aws cloudfront create-invalidation \
  --distribution-id "${CF_DISTRIBUTION_ID:?CF_DISTRIBUTION_ID not set}" \
  --paths "/*"

log "done — https://$BUCKET"
