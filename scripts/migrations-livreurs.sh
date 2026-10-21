#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Paiement à la livraison et fiches livreurs. À lancer avant de démarrer l'API ; une migration déjà générée est sautée.

add_migration() {
  local project="src/Modules/$1/$1.Infrastructure"
  if ls "$project/Persistence/Migrations/"*"_$2.cs" >/dev/null 2>&1; then
    echo "$1 : $2 déjà générée"
    return
  fi
  dotnet restore "$project"
  dotnet ef migrations add "$2" --project "$project" --output-dir Persistence/Migrations
}

# Fiches livreurs (delivery.couriers), montant à encaisser et encaissé sur les livraisons (fulfillments.courier_id,
# courier_fee, amount_to_collect, amount_collected, collection_method, remittance_id), remises des livreurs
# (delivery.courier_remittances, courier_remittance_items avec index unique sur la fiche).
add_migration Delivery CouriersAndCollection

# Moyen « espèces encaissées par le livreur » (26) : contraintes payments.*.method. Le moyen intégré courier_cash est
# posé au démarrage.
add_migration Payments CashOnDeliveryMethod
