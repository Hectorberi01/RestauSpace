#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Fonctionnalités P1 : une migration par module modifié. À lancer avant de démarrer l'API ; une migration déjà
# générée est sautée, le script peut donc être relancé après chaque livraison.

add_migration() {
  local project="src/Modules/$1/$1.Infrastructure"
  if ls "$project/Persistence/Migrations/"*"_$2.cs" >/dev/null 2>&1; then
    echo "$1 : $2 déjà générée"
    return
  fi
  dotnet restore "$project"
  dotnet ef migrations add "$2" --project "$project" --output-dir Persistence/Migrations
}

# Caisse tactile : PIN de caisse des membres, caisses partagées (clé d'appareil), pointages.
add_migration Staff TillsAndTimeClock

# Addition partagée par article : lignes de commande réglées par chaque paiement (payments.covered_items).
add_migration Payments PaymentCoveredItems

# QR de table signé : jeton aléatoire par table (floor.restaurant_tables.qr_token, unique).
add_migration Floor TableQrTokens

# Coût matière et achats : coût moyen des articles, coût des réceptions, fournisseurs, bons de commande.
add_migration Inventory CostsAndPurchasing

# Campagnes marketing : envois groupés par lots et règle de fréquence du restaurant.
add_migration Notifications CampaignsAndMarketingRules

# Réponses aux questionnaires (anonymes, une par commande au plus).
add_migration Reviews SurveyResponses

# CRM : notes de l’équipe, fusion de doublons (lien vers la fiche gardée).
add_migration Customers NotesAndMerge

# Réservation en ligne : choix de chaque établissement (location_policies.accepts_online_bookings).
add_migration Reservations OnlineBooking

# Abonnement : règlements en ligne (subscription_checkouts), numéro de facture et prestataire des paiements, suite des numéros par année.
add_migration Billing OnlinePaymentAndInvoices
