#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Identity/Identity.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Tenants/Tenants.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Locations/Locations.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Staff/Staff.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Catalog/Catalog.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Floor/Floor.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Reservations/Reservations.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Orders/Orders.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Kitchen/Kitchen.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Payments/Payments.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Inventory/Inventory.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Customers/Customers.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Notifications/Notifications.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Promotions/Promotions.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Loyalty/Loyalty.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Billing/Billing.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Reporting/Reporting.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Files/Files.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Integrations/Integrations.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations add OutboxDeliveriesAndProjections --project src/Modules/Reviews/Reviews.Infrastructure --output-dir Persistence/Migrations
