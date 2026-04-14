#!/usr/bin/env bash
# Pulls the latest CrossCloudKit copilot instructions from upstream.
# Run manually, via the VS Code task "sync: copilot instructions",
# or automatically on every `git pull` (via .githooks/post-merge).
#
# First-time setup (once per clone):
#   git config core.hooksPath .githooks

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TARGET_DIR="$REPO_ROOT/.github"
TARGET_FILE="$TARGET_DIR/copilot-instructions.md"
SOURCE_URL="https://raw.githubusercontent.com/bkio/CrossCloudKit/main/.github/copilot-instructions.md"

mkdir -p "$TARGET_DIR"
curl -fsSL -o "$TARGET_FILE" "$SOURCE_URL"
echo "✓ Synced $TARGET_FILE from upstream"
