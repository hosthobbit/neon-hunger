#!/usr/bin/env bash
# Builds the browser version and copies it into docs/, the folder GitHub Pages serves from main.
# GitHub's hosted runners have no Unity licence, so the build is made locally and committed; every
# push to main then triggers GitHub's own "pages build and deployment" run, which publishes docs/.
# Run this after changing anything under Assets/, then commit docs/ along with your source.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
./build-web.sh
grep -q "NH_WEBGL_OK" Logs/build-web.log || { echo "WebGL build failed - see Logs/build-web.log" >&2; exit 1; }
# replace the previous build but keep the README screenshots that also live in docs/
rsync -a --delete --exclude screenshots/ --exclude .nojekyll Build/WebGL/ docs/
touch docs/.nojekyll
du -sh docs
echo "docs/ updated. Commit it and push to main to publish."
