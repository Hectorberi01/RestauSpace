#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet restore src/Modules/Catalog/Catalog.Infrastructure
dotnet ef migrations add OnlineCatalog --project src/Modules/Catalog/Catalog.Infrastructure --output-dir Persistence/Migrations
