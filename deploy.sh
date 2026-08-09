#!/usr/bin/env bash
# Pull, rebuild and restart, stamping the image with the commit it was built from.
#
# The stamp has to be passed in: .dockerignore excludes .git, so the build cannot work out
# its own commit. Building by hand without it leaves the version reading "unknown", which
# is why this script exists rather than a line in the README that is easy to skip.
set -euo pipefail

cd "$(dirname "$0")"

echo "==> pulling"
git pull --ff-only

GIT_COMMIT="$(git rev-parse --short HEAD)"
BUILT_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
export GIT_COMMIT BUILT_AT

echo "==> building $GIT_COMMIT"
docker compose up -d --build

echo "==> waiting for health"
for _ in $(seq 1 30); do
    if curl -fsS http://localhost:8080/healthz >/dev/null 2>&1; then
        break
    fi
    sleep 2
done

echo "==> live:"
curl -fsS http://localhost:8080/healthz || echo "  health check did not respond — check 'docker compose logs'"
echo
