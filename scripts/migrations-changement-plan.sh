#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Billing : colonne subscriptions.scheduled_plan_id (changement de plan programmé à l'échéance).
dotnet restore src/Modules/Billing/Billing.Infrastructure
dotnet ef migrations add ScheduledPlanChange --project src/Modules/Billing/Billing.Infrastructure --output-dir Persistence/Migrations
