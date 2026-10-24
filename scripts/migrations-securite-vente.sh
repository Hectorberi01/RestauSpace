#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# online_orders : customer_id, client_key et l'index des commandes en attente de paiement (limite des commandes impayées).
dotnet restore src/Modules/Storefront/Storefront.Infrastructure
dotnet ef migrations add PendingPaymentLimits --project src/Modules/Storefront/Storefront.Infrastructure --output-dir Persistence/Migrations
