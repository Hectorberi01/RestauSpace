#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet restore src/Modules/Billing/Billing.Infrastructure
dotnet ef migrations add SubscriptionPaymentLines --project src/Modules/Billing/Billing.Infrastructure --output-dir Persistence/Migrations
