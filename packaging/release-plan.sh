#!/usr/bin/env bash
set -euo pipefail

publish=false
case "$GITHUB_EVENT_NAME" in
  push)
    # The association can lag the push event. A direct push has no matching merged PR.
    number=''
    for attempt in {1..6}; do
      pulls=$(gh api --paginate --slurp "repos/$GITHUB_REPOSITORY/commits/$GITHUB_SHA/pulls")
      number=$(jq -r --arg sha "$GITHUB_SHA" --arg base "$GITHUB_REF_NAME" '
        [.[][] | select(.base.ref == $base and .merged_at != null and .merge_commit_sha == $sha)]
        | first | .number // empty' <<<"$pulls")
      if [[ -n "$number" ]]; then break; fi
      if (( attempt < 6 )); then sleep 3; fi
    done
    if [[ -z "$number" ]]; then
      echo "No merged pull request is associated with this commit."
      echo 'build=false' >> "$GITHUB_OUTPUT"
      exit 0
    fi
    tag="v0.1.0-pr.$number"
    publish=true
    ;;
  *)
    tag="v0.1.0-preview.$GITHUB_RUN_NUMBER"
    ;;
esac

{
  echo 'build=true'
  echo "publish=$publish"
  echo "tag=$tag"
  echo "image=ghcr.io/${GITHUB_REPOSITORY,,}"
} >> "$GITHUB_OUTPUT"
