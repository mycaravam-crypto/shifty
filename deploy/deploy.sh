#!/usr/bin/env bash
# Deploy the current git revision as new docker-compose images, then run
# pending EF Core migrations. Rollback = check out the previous revision
# and run this script again (docker images are already versioned by tag,
# unlike vanspace3d's rsync/releases/<timestamp> scheme).
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
