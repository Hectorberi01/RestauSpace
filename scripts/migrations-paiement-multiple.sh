#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Plusieurs prestataires proposés par devise pour payer l’abonnement (après migrations-abonnements.sh). À lancer avant de démarrer
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

# Prestataires suivants proposés pour une devise (billing.online_payment_routes.other_providers, tableau vide par défaut).
add_migration Billing SubscriptionPaymentProviders
