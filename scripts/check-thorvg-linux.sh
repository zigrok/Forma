#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="forma-thorvg-linux:local"

docker build --platform linux/amd64 --file "$repo_root/scripts/Dockerfile.thorvg-linux" --tag "$image" "$repo_root"
# Inside a self-hosted runner container (Docker-outside-of-docker) the checkout is not a path the Docker host knows, so the
# container borrows this container's volumes and runs in the checkout instead of mounting it at /repo.
if [[ -n "${DOCKER_HOST:-}" ]]; then
    # A job container with its own Docker daemon: neither this checkout nor this container exists in that daemon, so the
    # image's copy of the checkout is used and no path is mounted.
    repo_mount=()
elif [[ -f /.dockerenv && -n "${HOSTNAME:-}" ]]; then
    repo_mount=(--volumes-from "$HOSTNAME" --workdir "$repo_root")
else
    repo_mount=(--volume "$repo_root:/repo")
fi
docker run --rm \
    --platform linux/amd64 \
    "${repo_mount[@]}" \
    --env MESON=/usr/local/bin/meson \
    --env THORVG_ARTIFACTS_DIR=/tmp/forma-thorvg-linux \
    "$image" \
    bash scripts/run-thorvg-linux-container.sh