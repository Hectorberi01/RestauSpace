#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet restore src/Modules/Storefront/Storefront.Infrastructure

dotnet ef migrations add StorefrontAddOns --project src/Modules/Billing/Billing.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OrderChannelAndSchedule --project src/Modules/Orders/Orders.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add InitialCreate --project src/Modules/Storefront/Storefront.Infrastructure --output-dir Persistence/Migrations
