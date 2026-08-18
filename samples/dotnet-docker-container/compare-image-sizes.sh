#!/usr/bin/env bash
# Builds the same app on three runtime base images and reports the actual sizes, plus a
# couple of checks for claims that are easy to get wrong.
#
#   ./compare-image-sizes.sh
#
# Needs a running Docker daemon. Requires network access on first run to pull base images.
set -uo pipefail

REPO=jorgenhoc-sample
declare -a VARIANTS=("debian:Dockerfile" "alpine:Dockerfile.alpine" "chiseled:Dockerfile.chiseled")

if ! docker info >/dev/null 2>&1; then
    echo "Docker daemon is not reachable. Start Docker Desktop and try again." >&2
    exit 1
fi

echo "Building ${#VARIANTS[@]} image variants — first run pulls base images, so allow a few minutes."
echo

for entry in "${VARIANTS[@]}"; do
    tag="${entry%%:*}"
    file="${entry#*:}"
    printf '  %-9s (%s) ... ' "$tag" "$file"
    if docker build -q -f "$file" -t "$REPO:$tag" . >/dev/null 2>"/tmp/build-$tag.err"; then
        echo "ok"
    else
        echo "FAILED — see /tmp/build-$tag.err"
    fi
done

mb() { awk -v b="$1" 'BEGIN { printf "%.0f", b/1024/1024 }'; }

echo
echo "Uncompressed image size (docker image inspect .Size)"
echo
printf '| %-28s | %-18s | %s\n' "Base image" "Uncompressed size" "Relative |"
printf '|%s|%s|%s\n' "------------------------------" "--------------------" "----------|"

base_size=""
for entry in "${VARIANTS[@]}"; do
    tag="${entry%%:*}"
    size=$(docker image inspect "$REPO:$tag" --format '{{.Size}}' 2>/dev/null) || continue
    [ -z "$base_size" ] && base_size="$size"
    rel=$(awk -v s="$size" -v b="$base_size" 'BEGIN { printf "%.2fx", s/b }')
    printf '| %-28s | %15s MB | %8s |\n' "aspnet:10.0${tag:+-$tag}" "$(mb "$size")" "$rel"
done

echo
echo "These are UNCOMPRESSED local sizes. Registry pull size is smaller because layers are"
echo "gzipped, and it depends on which layers you already have cached — so it is not a"
echo "property of the image alone and is not reported here."

echo
echo "Claim check 1: is wget or curl present for a HEALTHCHECK?"
for tag in debian alpine; do
    printf '  %-9s ' "$tag"
    docker run --rm --entrypoint sh "$REPO:$tag" -c \
        'for t in wget curl; do command -v $t >/dev/null 2>&1 && printf "%s found " $t; done; echo' \
        2>/dev/null || echo "(could not run shell)"
done
printf '  %-9s ' "chiseled"
docker run --rm --entrypoint sh "$REPO:chiseled" -c 'echo shell works' 2>/dev/null \
    || echo "no shell at all — HEALTHCHECK CMD cannot work"

echo
echo "Claim check 2: does the container actually run as a non-root user?"
for entry in "${VARIANTS[@]}"; do
    tag="${entry%%:*}"
    printf '  %-9s ' "$tag"
    cid=$(docker run -d -p 0:8080 "$REPO:$tag" 2>/dev/null) || { echo "start failed"; continue; }
    sleep 3
    port=$(docker port "$cid" 8080/tcp 2>/dev/null | head -1 | sed 's/.*://')
    if [ -n "${port:-}" ]; then
        curl -s -m 5 "http://localhost:$port/" || echo -n "(no response)"
        echo
    else
        echo "(no port mapping)"
    fi
    docker rm -f "$cid" >/dev/null 2>&1
done
