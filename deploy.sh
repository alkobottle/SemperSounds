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

# Ask compose which host port it actually published rather than assuming 8080. On a host
# where something else already owns that port — a Pi-hole admin server, say — compose.yaml
# is edited locally to publish elsewhere, and a hardcoded probe then health-checks whatever
# else is listening. That fails as a 404 from a stranger while the app is perfectly fine,
# which is a genuinely confusing way to end a deploy.
# A wildcard bind prints as 0.0.0.0 or [::]; reach those over loopback.
PUBLISHED="$(docker compose port sempersounds 8080 2>/dev/null | tail -1 | sed -E 's/^(0\.0\.0\.0|\[::\]):/127.0.0.1:/')"
HEALTH_URL="http://${PUBLISHED:-127.0.0.1:8080}/healthz"

echo "==> waiting for health at $HEALTH_URL"
for _ in $(seq 1 30); do
    if curl -fsS "$HEALTH_URL" >/dev/null 2>&1; then
        break
    fi
    sleep 2
done

echo "==> live:"
curl -fsS "$HEALTH_URL" || echo "  health check did not respond — check 'docker compose logs'"
echo
