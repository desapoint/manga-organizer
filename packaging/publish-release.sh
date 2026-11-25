#!/usr/bin/env bash
set -euo pipefail

artifacts=${1:?Pass the directory containing the packaged release assets}

if existing=$(gh release view "$TAG" --json isDraft,tagName --jq '.isDraft' 2>/dev/null) && [[ "$existing" == false ]]; then
  actual_sha=$(gh api "repos/$GITHUB_REPOSITORY/commits/$TAG" --jq .sha)
  [[ "$actual_sha" == "$GITHUB_SHA" ]]
  echo "The prerelease $TAG already exists."
  exit 0
fi

refs=()
for architecture in amd64 arm64; do
  docker load --input "$artifacts/MangaOrganizer-docker-$architecture.tar.gz"
  arch_ref="$IMAGE:$TAG-$architecture"
  docker tag "$IMAGE:$TAG" "$arch_ref"
  docker push "$arch_ref"
  digest_ref=$(docker image inspect --format '{{index .RepoDigests 0}}' "$arch_ref")
  refs+=("$digest_ref")
done
docker buildx imagetools create --tag "$IMAGE:$TAG" "${refs[@]}"
manifest=$(docker buildx imagetools inspect "$IMAGE:$TAG" --format '{{json .Manifest}}')
digest=$(jq -er '
  select(([.manifests[].platform | select(.os == "linux") | .architecture] | sort) == ["amd64", "arm64"])
  | .digest | select(test("^sha256:[a-f0-9]{64}$"))' <<<"$manifest")

python packaging/docker/bundle.py "$artifacts" "$IMAGE:$TAG@$digest" "$IMAGE:$TAG" "$GITHUB_SERVER_URL/$GITHUB_REPOSITORY"
cp docs/INSTALL.md "$artifacts/INSTALL.md"
( cd "$artifacts" && sha256sum -- *.zip *.tar.gz *.exe *.xml INSTALL.md > SHA256SUMS.txt )

if [[ "${existing:-}" != true ]]; then
  gh release create "$TAG" --target "$GITHUB_SHA" --draft --prerelease \
    --title "Manga Organizer $TAG" --generate-notes \
    --notes "Automatic development build of \`$GITHUB_SHA\`. See INSTALL.md for the Windows installer, portable archives, Docker Compose and Unraid."
fi
gh release upload "$TAG" "$artifacts"/* --clobber
gh release edit "$TAG" --draft=false --prerelease --latest=false
