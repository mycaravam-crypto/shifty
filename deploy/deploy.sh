#!/usr/bin/env bash
# Deploy the current git revision as new docker-compose images, then run
# pending EF Core migrations. Rollback = check out the previous revision
# and run this script again (docker images are already versioned by tag,
# unlike vanspace3d's rsync/releases/<timestamp> scheme).
#
# Each run also bumps frontend/package.json's patch version and commits
# that bump — this is the "vX.Y.Z" shown subtly in the app UI (App.vue),
# same scheme as vanspace3d. Locally the commit is left for you to push;
# under CI it's pushed back automatically, tagged "[skip ci]" so it
# doesn't retrigger the workflow.
#
# Usage:
#   SHIFTPLANNER_DEPLOY_HOST=user@your-server.example \
#   SHIFTPLANNER_DEPLOY_PATH=/srv/shiftplanner \
#     ./deploy/deploy.sh
#
# Requires: SSH key access to the host already set up, and a .env file
# already present at $SHIFTPLANNER_DEPLOY_PATH/.env on the server (see
# .env.example).

set -euo pipefail

HOST="${SHIFTPLANNER_DEPLOY_HOST:?Set SHIFTPLANNER_DEPLOY_HOST, e.g. user@your-server.example}"
REMOTE_PATH="${SHIFTPLANNER_DEPLOY_PATH:?Set SHIFTPLANNER_DEPLOY_PATH, e.g. /srv/shiftplanner}"

echo "Bumping version..."
NEW_VERSION="$(cd frontend && npm version patch --no-git-tag-version | sed 's/^v//')"
echo "Version: $NEW_VERSION"

echo "Committing version bump..."
if ! git config user.email >/dev/null 2>&1; then
    git config user.email "deploy-bot@shifty.local"
    git config user.name "ShiftPlanner Deploy Bot"
fi
git add frontend/package.json frontend/package-lock.json
git commit -m "Release v$NEW_VERSION [skip ci]"
if [ "${CI:-}" = "true" ]; then
    echo "Pushing version bump (CI run)..."
    git push
fi

echo "Syncing repo to $HOST:$REMOTE_PATH ..."
rsync -avz --delete \
  --exclude .git --exclude .env --exclude 'bin' --exclude 'obj' \
  --exclude 'node_modules' --exclude 'dist' \
  ./ "$HOST:$REMOTE_PATH/"

echo "Building and starting containers ..."
ssh "$HOST" "cd '$REMOTE_PATH' && docker compose build && docker compose up -d"

echo "Applying database migrations ..."
ssh "$HOST" "cd '$REMOTE_PATH' && docker compose exec -T api dotnet ShiftPlanner.Api.dll --migrate"

echo "Pruning dangling images ..."
ssh "$HOST" "docker image prune -f"

echo "Done."
