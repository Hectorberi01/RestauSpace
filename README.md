# SaaS Restaurant — backend

Monolithe modulaire .NET 10 / PostgreSQL : 21 modules métier isolés, Clean Architecture dans chaque module, DDD
pragmatique, multi-tenant dès la conception. Les modules pourront être extraits en services plus tard sans réécriture.

- Bases métier de chaque module (agrégats, règles, événements, dépendances, état) : [`docs/MODULES.md`](docs/MODULES.md)
- Détail classe par classe d'un module : `src/Modules/<Module>/README.md`
- Lancer l'API en local, configuration : [`src/Host/README.md`](src/Host/README.md)

## Structure

```
SaasRestaurant.sln
Directory.Build.props          net10.0, nullable, warnings = erreurs, versions de packages centralisées
Directory.Packages.props
docker-compose.yml             PostgreSQL local (port 5433)
docs/
  MODULES.md                   catalogue métier des 21 modules
src/
  BuildingBlocks/              petits et génériques, AUCUN métier restaurant
    SaasRestaurant.BuildingBlocks.Domain           AggregateRoot, Entity, ValueObject, DomainEvent, DomainError,
                                                   TenantId/ITenantOwned, Money/Currency, Email/PhoneNumber,
                                                   IClock, Period, TimeZoneId, ids partagés (OrderId, ProductId…)
    SaasRestaurant.BuildingBlocks.Application      ICommand/IQuery + handlers, IUnitOfWork, exceptions applicatives
    SaasRestaurant.BuildingBlocks.Infrastructure   Outbox (message, dispatcher, unit of work transactionnel)
    SaasRestaurant.BuildingBlocks.Api              ProblemDetails, mapping DomainError -> HTTP, enregistrement handlers
  Modules/
    <Module>/
      README.md                frontière d'agrégat, classes, transitions, erreurs, choix
      <Module>.Domain          agrégats, entités, VO, events, ports métier ; ne dépend que de BuildingBlocks.Domain
      <Module>.Application     commandes / requêtes, DTO, ports (repositories, I<Module>UnitOfWork), erreurs applicatives
      <Module>.Infrastructure  DbContext (schéma <module>), configurations EF, migrations, repositories, providers
      <Module>.Api             <Module>Module (composition) + endpoints ; appelé par le Host
  Host/
    SaasRestaurant.Host        le seul exécutable : compose les modules, auth, autorisation, outbox, health checks
Tests/
  SaasRestaurant.Testing       FakeClock, DomainAssert
  BuildingBlocks/              tests du kernel
  Modules/<Module>/<Module>.<Couche>/   tests par module et par couche (Domain partout ; App/Infra/Api où c'est écrit)
  Architecture/                règles d'architecture vérifiées sur les assemblies compilées
  Host/                        le vrai Program sur un vrai PostgreSQL (Testcontainers)
```

Modules (ordre de construction du prompt maître) : Identity, Tenants, Locations, Staff, Catalog, Floor, Reservations,
Orders, Kitchen, Payments, Inventory, Customers, Notifications, Promotions, Loyalty, Billing, Reporting, Files,
Integrations, Reviews, Audit.

**État** : Domain + tests écrits pour les 21 modules (≈ 2 600 tests Domain). Identity, Tenants, Locations, Staff,
Catalog, Floor, Reservations, Orders, Kitchen et Payments sont complets de bout en bout (Payments : migration initiale
à générer, voir son README). Les 11 autres ont leurs couches Application / Infrastructure / Api créées et câblées dans le Host, vides : on les
remplit module par module, dans l'ordre ci-dessus.

## Règles (vérifiées par `Tests/Architecture` quand c'est mécanisable)

| Règle | Vérifiée par |
|---|---|
| Domain ne référence ni EF Core, ni ASP.NET Core, ni Npgsql, Redis, Kafka, `Microsoft.Extensions.*`, ni les autres couches | `LayerDependencyTests` |
| Application ne référence ni EF Core, ni ASP.NET Core, ni Infrastructure / Api | `LayerDependencyTests` |
| Infrastructure ne référence pas l'Api | `LayerDependencyTests` |
| Aucun module ne référence l'assembly d'un autre module, à aucune couche | `LayerDependencyTests` |
| Les BuildingBlocks ne contiennent aucun module | `LayerDependencyTests` |
| Tout agrégat porte son `TenantId` (sauf `User`, `Tenant`, `Plan` : plateforme) | `DomainModelTests` |
| Aucun setter public, aucune collection mutable exposée sur les entités | `DomainModelTests` |
| Les domain events sont immuables | `DomainModelTests` |
| Un module ne lit jamais les tables ou le DbContext d'un autre : un schéma PostgreSQL par module | revue + schéma par module |
| `TenantId` vient du jeton d'accès (claim `tid`, posé après contrôle d'appartenance), jamais de la requête | `ITenantContext` + tests d'Identity §11 |
| Une table tenant-owned ne montre que le tenant courant, et rien sans tenant | `TenantDbContext` (filtre EF Core) + `TenantQueryFilterTests` |
| Une action protégée exige une permission du rôle Staff de l'utilisateur, revérifiée à chaque requête | `RequirePermission(...)` + `IPermissionChecker` (Staff) |

**Communication entre modules** :

- *synchrone* : le module qui a besoin d'une information déclare un **port** dans son Domain ou son Application
  (ex. `ITenantPricedCatalogChecker` dans Tenants, `ICatalogLocations` dans Catalog) ; le Host l'implémente
  en appelant l'Application de l'autre module. Les valeurs reçues entrent dans le domaine sous forme de **références
  locales** (`LocationReference`, `CustomerReference`…) ou de **snapshots** (`ProductSnapshot`), jamais d'objets d'un
  autre module ;
- *asynchrone* : domain events → outbox (même transaction) → dispatcher → consommateurs idempotents. Kafka n'arrivera
  que pour les flux qui en ont vraiment besoin (notifications, analytics, webhooks sortants) ; il ne remplace pas un
  appel interne simple.

## Ajouter un cas d'usage dans un module (modèle : Tenants)

1. `Application/Abstractions` : port du repository (`I<Agrégat>Repository`), `I<Module>UnitOfWork` existe déjà.
2. `Application/Commands|Queries` : commande + handler (`ICommandHandler<,>`), DTO de lecture ; aucune règle métier
   ici, seulement chargement → appel de la méthode métier → sauvegarde.
3. `Infrastructure/Persistence` : `<Module>DbContext : TenantDbContext` qui appelle `ApplyTenantQueryFilters` à la fin
   d'`OnModelCreating` (schéma `DependencyInjection.Schema`, table d'outbox),
   `IEntityTypeConfiguration` par agrégat (index, unicité, jeton de concurrence, `decimal` à précision explicite),
   repository, `<Module>UnitOfWork : OutboxUnitOfWork<>`, puis enregistrement dans `Add<Module>Infrastructure`.
4. Migration nommée :
   `dotnet ef migrations add InitialCreate -p src/Modules/<Module>/<Module>.Infrastructure -o Persistence/Migrations`
5. `Api` : `<Module>Endpoints` + requêtes/réponses ; mapping dans `Program.cs` avec la policy
   `Tenancy.TenantMemberPolicy` ; le handler prend le tenant de `ITenantContext.RequireTenant()`.
6. Tests : Application (fakes), Infrastructure (PostgreSQL Testcontainers, contraintes + isolation tenant), Api.

## Commandes

```bash
docker compose up -d
dotnet build
dotnet test                                                   # Docker requis pour les tests Infrastructure / Host
dotnet test Tests/Architecture/SaasRestaurant.ArchitectureTests
dotnet run --project src/Host/SaasRestaurant.Host
```

Notifications : générer la première migration (une fois) :

```bash
NOTIFICATIONS_DB="Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant" \
  dotnet ef migrations add InitialCreate \
  --project src/Modules/Notifications/Notifications.Infrastructure --output-dir Persistence/Migrations
```

Conformité et sécurité du module : `docs/NOTIFICATIONS-CONFORMITE.md`, `docs/NOTIFICATIONS-SECURITE.md`.
