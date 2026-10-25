#!/bin/bash
# LAW enforcement: the public-clean gate runs BEFORE every push of this branch.
# The 2026-10-21 leak (employer paths and one repo name inside debug.md command
# blocks) shipped because pushes ran `git push` directly and skipped the gate.
# Push through this script only: tools/push-trial.sh [remote] [branch]
set -eu
cd "$(dirname "$0")/.."
bash tools/check-public-clean.sh
remote="${1:-public}"
branch="${2:-trial/scope-prework}"
exec git push "$remote" "$branch"
