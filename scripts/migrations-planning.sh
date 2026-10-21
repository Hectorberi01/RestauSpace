#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Planning de l'équipe. À lancer avant de démarrer l'API ; une migration déjà générée est sautée.

add_migration() {
  local project="src/Modules/$1/$1.Infrastructure"
  if ls "$project/Persistence/Migrations/"*"_$2.cs" >/dev/null 2>&1; then
    echo "$1 : $2 déjà générée"
    return
  fi
  dotnet restore "$project"
  dotnet ef migrations add "$2" --project "$project" --output-dir Persistence/Migrations
}

# Semaines de planning (staff.schedule_weeks : brouillon et version publiée en jsonb), réglages par établissement
# (staff.schedule_settings), absences (staff.time_off_requests) et remplacements (staff.shift_swap_requests).
add_migration Staff TeamSchedule
