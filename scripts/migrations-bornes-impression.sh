#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet restore src/Modules/Printing/Printing.Infrastructure
dotnet restore src/Modules/Storefront/Storefront.Infrastructure
dotnet ef migrations add InitialCreate --project src/Modules/Printing/Printing.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add Kiosks --project src/Modules/Storefront/Storefront.Infrastructure --output-dir Persistence/Migrations
