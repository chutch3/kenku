#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Provide Migration-name" >&2
  exit 128
fi

readonly name="$1"

dotnet ef migrations add "$name" --context SeriesContext --output-dir Migrations/Manga
dotnet ef migrations add "$name" --context LibraryContext --output-dir Migrations/Library
dotnet ef migrations add "$name" --context NotificationsContext --output-dir Migrations/Notifications
dotnet ef migrations add "$name" --context ActionsContext --output-dir Migrations/Actions
dotnet ef migrations add "$name" --context JobsContext --output-dir Migrations/Jobs
dotnet ef migrations add "$name" --context DiscoveryContext --output-dir Migrations/Discovery
