#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Plans offerts et prestataire de paiement par devise (après migrations-etablissements.sh). À lancer avant de démarrer
# l'API ; une migration déjà générée est sautée.

add_migration() {
  local project="src/Modules/$1/$1.Infrastructure"
  if ls "$project/Persistence/Migrations/"*"_$2.cs" >/dev/null 2>&1; then
    echo "$1 : $2 déjà générée"
    return
  fi
  dotnet restore "$project"
  dotnet ef migrations add "$2" --project "$project" --output-dir Persistence/Migrations
}

# Plan offert (billing.subscriptions.is_complimentary, ending_notice_sent_for_utc) et prestataire des abonnements par
# devise (billing.online_payment_routes).
add_migration Billing ComplimentaryAndPaymentRoutes
