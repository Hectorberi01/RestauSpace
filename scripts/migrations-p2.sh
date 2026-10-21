#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Fonctionnalités P2 : une migration par module modifié. À lancer avant de démarrer l'API ; une migration déjà
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

# Anti-robot (Turnstile) activable par application de vente (sales_apps.requires_human_check).
add_migration Storefront HumanCheck

# Préférences par type d'alerte d'équipe (notification_preferences.muted_types).
add_migration Notifications AlertTypePreferences

# Salle avancée : place et forme des tables sur le plan dessiné (restaurant_tables.layout_*).
add_migration Floor TableLayouts

# Envoi par service : service des produits (station_routings.courses), plats en attente (kitchen_ticket_items.course, is_held).
add_migration Kitchen Courses

# Miniatures des photos, préparées par le navigateur (media_files.thumbnail_of).
add_migration Files Thumbnails

# Prix par établissement et par canal (products.price_overrides) ; cartes par créneau (menus.slots).
add_migration Catalog PricesAndMenuSlots

# Validation en deux étapes : secret chiffré, codes de secours (empreintes), dernier pas utilisé (users.two_factor_*).
add_migration Identity TwoFactor

# Cartes cadeaux et avoirs (loyalty.stored_value_cards : empreinte du code, solde, mouvements).
add_migration Loyalty StoredValueCards

# Moyens « carte cadeau / avoir » (24) et « payé à la plateforme » (25) : contraintes payments.*.method.
add_migration Payments GiftCardAndPlatformMethods

# Commandes des plateformes de livraison (orders.platform, orders.external_reference, index unique).
add_migration Orders DeliveryPlatforms

# Rotation des tables sur le plan dessiné (restaurant_tables.layout_rotation).
add_migration Floor TableRotation

# Exports et rapports planifiés demandés par une clé d'API (export_jobs.requested_by_kind, report_deliveries.scheduled_by_kind).
add_migration Reporting ApiKeyRequesters

# Site du restaurant : coordonnées publiques des établissements (locations.contact).
add_migration Locations PublicContact

# Présentation publique du restaurant : accroche, description, couverture, liens (tenants.public_profile).
add_migration Tenants PublicProfile

# Avis publiés sur le site (feedbacks.published_at_utc, contrainte et index partiel).
add_migration Reviews PublishedFeedbacks
