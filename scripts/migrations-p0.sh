#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Caisse (sessions, ticket Z) : table payments.cash_sessions.
dotnet restore src/Modules/Payments/Payments.Infrastructure
dotnet ef migrations add CashSessions --project src/Modules/Payments/Payments.Infrastructure --output-dir Persistence/Migrations

# Impression : rôle cuisine des imprimantes (bons, poste servi), nouveaux types de tickets (bon de cuisine, reçu).
dotnet restore src/Modules/Printing/Printing.Infrastructure
dotnet ef migrations add KitchenTickets --project src/Modules/Printing/Printing.Infrastructure --output-dir Persistence/Migrations

# Facture normalisée e-MECeF : nouveau module Fiscal (schéma fiscal). Ajouter Fiscal:TokenKey (32 octets, base64) en production.
dotnet sln SaasRestaurant.sln add --solution-folder Fiscal \
  src/Modules/Fiscal/Fiscal.Domain/Fiscal.Domain.csproj src/Modules/Fiscal/Fiscal.Application/Fiscal.Application.csproj \
  src/Modules/Fiscal/Fiscal.Infrastructure/Fiscal.Infrastructure.csproj src/Modules/Fiscal/Fiscal.Api/Fiscal.Api.csproj \
  Tests/Modules/Fiscal/Fiscal.Domain/Fiscal.Domain.Tests.csproj Tests/Modules/Fiscal/Fiscal.Application/Fiscal.Application.Tests.csproj \
  Tests/Modules/Fiscal/Fiscal.Infrastructure/Fiscal.Infrastructure.Tests.csproj
dotnet restore src/Modules/Fiscal/Fiscal.Infrastructure
dotnet ef migrations add InitialCreate --project src/Modules/Fiscal/Fiscal.Infrastructure --output-dir Persistence/Migrations
