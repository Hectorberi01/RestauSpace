#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Acompte de réservation et listes d'attente. À lancer avant de démarrer l'API ; une migration déjà générée est sautée.

add_migration() {
  local project="src/Modules/$1/$1.Infrastructure"
  if ls "$project/Persistence/Migrations/"*"_$2.cs" >/dev/null 2>&1; then
    echo "$1 : $2 déjà générée"
    return
  fi
  dotnet restore "$project"
  dotnet ef migrations add "$2" --project "$project" --output-dir Persistence/Migrations
}

# Acompte des réservations (reservations.deposit_*), règle d'acompte et réglages de liste d'attente par établissement
# (location_policies.deposit_*, waitlist_*), inscriptions en liste d'attente (reservations.waitlist_entries).
add_migration Reservations DepositsAndWaitlist

# Acompte payé : paiement rattaché à une réservation (payments.purpose, reservation_id, applied_at_utc).
add_migration Payments ReservationDeposits

# File d'attente à la porte (floor.waiting_parties).
add_migration Floor WaitingParties
