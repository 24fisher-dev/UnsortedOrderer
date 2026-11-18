#!/usr/bin/env bash
set -euo pipefail

if [[ ${GITHUB_EVENT_NAME:-} != push || ${GITHUB_REF:-} != refs/heads/main ]]; then
  echo 'Releases can only be published by a push to main.' >&2
  exit 1
fi

shopt -s nullglob
packages=("$1"/UnsortedOrderer_v*.exe)
if (( ${#packages[@]} != 1 )) || [[ ! -s ${packages[0]} ]]; then
  echo 'Expected exactly one non-empty standalone executable.' >&2
  exit 1
fi

package=${packages[0]}
name=${package##*/}
if [[ ! $name =~ ^UnsortedOrderer_(v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\.exe$ ]]; then
  echo "Invalid package name: $name" >&2
  exit 1
fi
tag=${BASH_REMATCH[1]}

# GitHub ignores --target for an existing tag, so never reuse a tag on another commit.
tag_ref=$(git ls-remote --refs origin "refs/tags/$tag")
if [[ -n $tag_ref && ${tag_ref%%$'\t'*} != "$GITHUB_SHA" ]]; then
  echo "Tag $tag belongs to a different commit." >&2
  exit 1
fi

# Resume an unfinished draft or leave a complete published release unchanged.
if release=$(gh release view "$tag" --json isDraft,targetCommitish,assets 2>/dev/null); then
  if [[ $(jq -r .targetCommitish <<<"$release") != "$GITHUB_SHA" ]]; then
    echo "Release $tag belongs to a different commit." >&2
    exit 1
  fi
  if [[ $(jq -r .isDraft <<<"$release") == false ]]; then
    jq -e --arg name "$name" '.assets | any(.name == $name and .size > 0)' <<<"$release" >/dev/null
    echo "Release $tag is already published with $name."
    exit 0
  fi
else
  gh release create "$tag" --target "$GITHUB_SHA" --title "UnsortedOrderer $tag" \
    --notes "Автономная версия для Windows x64. Скачайте и запустите \`$name\`. Установка .NET не нужна." --draft
fi

gh release upload "$tag" "$package" --clobber

# If an older build finishes later, do not replace the current main build as Latest.
main_sha=$(gh api "repos/$GH_REPO/git/ref/heads/main" --jq .object.sha)
latest=false
if [[ $main_sha == "$GITHUB_SHA" ]]; then latest=true; fi
gh release edit "$tag" --draft=false --latest="$latest"
