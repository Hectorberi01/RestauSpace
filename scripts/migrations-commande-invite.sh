#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet restore src/Modules/Storefront/Storefront.Infrastructure
dotnet ef migrations add OnlineOrders --project src/Modules/Storefront/Storefront.Infrastructure --output-dir Persistence/Migrations
