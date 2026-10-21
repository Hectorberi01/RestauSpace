# Host

Le seul exécutable : il compose les 21 modules et porte ce qui est transversal. Identity, Tenants, Locations et Staff
exposent leurs routes ; les 17 autres sont enregistrés (`Add<Module>Module`) mais n'ont encore ni persistance ni endpoint.

## Lancer en local

```bash
docker compose up -d                                   # PostgreSQL sur le port 5433
dotnet run --project src/Host/SaasRestaurant.Host      # http://localhost:5080, migrations appliquées au démarrage
```

Obtenir un jeton d'administrateur en développement (`admin@example.com` est listé dans
`appsettings.Development.json`) :

```bash
curl -H 'Content-Type: application/json' -d '{"email":"admin@example.com","password":"<mot de passe>"}' localhost:5080/auth/register
# l'email est écrit dans les logs du host ("[DEV EMAIL] to admin@example.com ...") : le token est dans le lien
curl -H 'Content-Type: application/json' -d '{"token":"<token>","password":"<mot de passe>"}' localhost:5080/auth/verify-email
curl -H 'Content-Type: application/json' -d '{"email":"admin@example.com","password":"<mot de passe>"}' localhost:5080/auth/login
curl -H "Authorization: Bearer <accessToken>" localhost:5080/me
```

## Ce que le host fournit

- **Authentification** : les jetons d'accès émis par le module Identity (`/auth/login`), validés avec les mêmes
  options que celles qui les signent (`AddIdentityAccessTokens()`).
- **Autorisation** :
  - `/health` et `/auth/*` sont ouverts ; `/me/*` exige un utilisateur connecté ;
  - `/tenants/*` et `/users/*` exigent la policy `PlatformAdmin` : un compte dont l'email **vérifié** figure dans
    `Authorization:PlatformAdminEmails`. La vérification compte : un compte peut changer son email pour une
    adresse listée avant d'avoir prouvé qu'il la possède.
- **Limite de débit** : `app.UseRateLimiter()` applique la limite par adresse cliente du module Identity sur
  `/auth/*`. **Derrière un proxy, il faut rétablir l'adresse réelle du client** (`UseForwardedHeaders`) : ce n'est
  pas fait, et sans cela tous les clients partagent le même budget.
- **Tenant de la requête** : `AddRequestTenantContext()` fait d'`ITenantContext` le claim `tid` du jeton d'accès
  (posé par `POST /me/tenant` après contrôle d'appartenance, voir le README d'Identity §11). Les routes qui agissent
  pour un restaurant exigent la policy `Tenancy.TenantMemberPolicy` (`tenant-member`) et lisent le tenant avec
  `ITenantContext.RequireTenant()`, jamais dans la requête. La réponse
  d'appartenance vient de Staff (`TenantMembershipsFromStaff`).
- **Logs** : `UseRequestLogScope()` ajoute `TenantId`, `UserId` et `CorrelationId` à tout log écrit pendant la requête.
- **Locations** : routes `/locations` derrière la policy `tenant-member` ; `TenantDirectoryForLocations` répond au
  port `ITenantDirectory` depuis le module Tenants (fuseau par défaut, écritures autorisées ou non).
- **Staff & RBAC** : `AddStaffModule(connectionString, <codes de permission des autres modules>)` ; ponts
  `StaffBridges.cs` (Staff -> Tenants, Locations, Identity ; Identity -> Staff pour `POST /me/tenant` : profil
  **actif** dans un tenant **non fermé**). Routes `/staff` (tenant-member + permission), `/me/memberships`
  (connecté), `/tenants/{id}/owners` (PlatformAdmin). `AddPermissionAuthorization()` évalue `RequirePermission(...)`.
- **`IClock`** : horloge système, toujours en UTC.
- **Emails d'Identity** : par SMTP dès que `Identity:Emails:Smtp:Host` est renseigné ; sinon, en développement
  seulement, `Identity:Emails:WriteToLog` les écrit dans les logs. Sans l'un ni l'autre, le host refuse de démarrer.
  Les trois liens `Identity:Emails:Links:*` (pages du front, avec `{token}`) sont obligatoires.
- **Tâches de fond** : publication des outbox (une par module complet), envoi des emails, purge périodique d'Identity.
- **Catalog** : routes `/catalog/...` derrière la policy `tenant-member` (écritures : `catalog.manage`) ; ponts
  `CatalogBridges.cs` : Catalog -> Tenants (devise par défaut, écritures autorisées), Catalog -> Locations
  (établissement d'un menu), et Tenants -> Catalog pour `ITenantPricedCatalogChecker` (la devise se verrouille dès
  qu'un produit ou un groupe d'options existe).
- **Floor** : routes `/floor/...` derrière la policy `tenant-member` (plan : `floor.manage`, service : `floor.serve`) ;
  ponts `FloorBridges.cs` : Floor -> Tenants (écritures autorisées), Floor -> Locations (établissement d'une zone).
  `ITableSessionOrdersChecker` est servi par Orders (`TableSessionOrdersFromOrders`).
- **Reservations** : routes `/reservations/...` derrière la policy `tenant-member` (`reservations.manage`,
  `reservations.configure`) ; ponts `ReservationsBridges.cs` : Reservations -> Tenants (écritures autorisées),
  -> Locations (établissement, horaires d'ouverture via `IsLocationOpenDuringQuery`), -> Floor (places des tables en
  service, sessions ouvertes = walk-ins, session d'une installation).
- **Orders** : routes `/orders/...` derrière la policy `tenant-member` (`orders.take`, `orders.process`,
  `orders.discount`) ; ponts `OrdersBridges.cs` : Orders -> Tenants (devise, mode TTC/HT, écritures autorisées),
  -> Locations, -> Floor (session de table), -> Catalog (`GetSellableItemQuery` : instantané d'une ligne), et Floor ->
  Orders (commandes ouvertes d'une session). `IOrderPayments` est servi par Payments (`OrderPaymentsFromPayments`).
- **Kitchen / KDS** : routes `/kitchen/...` derrière la policy `tenant-member` (`kitchen.configure`,
  `kitchen.operate`) ; ponts `KitchenBridges.cs` : Kitchen -> Tenants (écritures autorisées), -> Locations, -> Catalog
  (le produit d'une route existe), -> Orders (`GetOrderLinesQuery` : lignes à préparer, sans prix).
  **`KitchenOrderFlow`** (consommateur d'outbox) relie les deux modules : `OrderAccepted` -> ticket créé,
  `OrderCancelled` -> ticket annulé, `KitchenPreparationStarted` / `KitchenTicketReady` -> commande en préparation /
  prête. Les refus métier sont journalisés et ignorés (sinon l'outbox resterait bloquée) ; les erreurs techniques
  sont retentées.
- **Payments** : routes `/payments/...` derrière la policy `tenant-member` (`payments.collect`, `payments.refund`,
  `payments.configure`), `/payments/webhooks/{provider}/{methodId}` **anonymes** (signature vérifiée avec le secret du
  restaurant) et `/platform/payment-providers` derrière la policy `PlatformAdmin` (catalogue de la plateforme) ; ponts
  `PaymentsBridges.cs` : Payments -> Tenants, -> Orders (`GetOrderByIdQuery`), et Orders -> Payments
  (`GetOrderSettledAmountQuery` avant la clôture). `Payments:CredentialsKey` (32 octets base64) est OBLIGATOIRE : il
  chiffre les identifiants marchands des restaurants. Intégration `sandbox` : `Payments:EnableSandbox` (en développement ;
  ailleurs il faut aussi `Payments:AllowSandboxOutsideDevelopment`, à réserver à la recette).
- **Tenant hors requête** : un traitement d'arrière-plan agit pour un tenant en appelant
  `BackgroundTenant.ActFor(tenantId)` dans **son propre scope** ; `ITenantContext` le renvoie alors, uniquement quand
  il n'y a pas de requête HTTP en cours (pendant une requête, le tenant vient toujours du jeton).
- **Erreurs** : `AddApiConventions()` + `UseExceptionHandler()` (problem details, code métier dans `code`).
- **Migrations** : appliquées au démarrage (Identity, Tenants, Locations, Staff, Catalog, Floor, Reservations, Orders, Kitchen puis Payments) si `Database:MigrateOnStartup` vaut `true` (le
  cas en développement, `false` par défaut). Avec plusieurs instances, les lancer comme étape de déploiement.

## Configuration

| Clé | Rôle |
|---|---|
| `ConnectionStrings:SaasRestaurant` | PostgreSQL (un schéma par module) ; obligatoire |
| `Database:MigrateOnStartup` | Appliquer les migrations au démarrage |
| `--migrate-only` (argument) | Appliquer les migrations puis s'arrêter (étape de déploiement, conteneur `migrations`) |
| `ReverseProxy:Enabled`, `:KnownNetworks`, `:ForwardLimit` | Derrière Traefik : adresse du client et schéma lus dans `X-Forwarded-*`, acceptés seulement des réseaux déclarés (par défaut 10/8, 172.16/12, 192.168/16) |
| `Identity:AccessTokens:Issuer`, `:Audience` | Émetteur et audience des jetons d'accès |
| `Identity:AccessTokens:SigningKey` | Clé HMAC en base64, 32 octets minimum. **Secret**, obligatoire hors développement. En développement, absente = clé aléatoire à chaque démarrage (les jetons d'accès ne survivent pas au redémarrage) |
| `Identity:AccessTokens:LifetimeMinutes` | Durée des jetons d'accès (défaut 15) |
| `Identity:RateLimiting:Auth:PermitLimit`, `:WindowSeconds` | Requêtes `/auth` autorisées par adresse cliente et par fenêtre (défaut 60 par 60 s) |
| `Identity:Emails:Smtp:Host`, `:Port`, `:Security`, `:Username`, `:Password`, `:FromAddress`, `:FromName` | Serveur SMTP (`Security` : `StartTls`, `SslOnConnect`, `None`). `Password` est un **secret** |
| `Identity:Emails:Links:VerifyEmail`, `:ResetPassword`, `:AcceptInvitation` | URL des pages du front, contenant `{token}` |
| `Identity:Emails:ProductName` | Nom affiché dans les emails |
| `Identity:Emails:WriteToLog` | Écrire les emails dans les logs (développement uniquement) |
| `Identity:Passwords:RejectCompromised` | Refuser les mots de passe connus de fuites (appels sortants vers api.pwnedpasswords.com) |
| `Authorization:PlatformAdminEmails` | Emails des administrateurs de la plateforme |
| `Payments:CredentialsKey` | Clé AES-256 (32 octets, base64) des identifiants marchands des restaurants. **Secret**, obligatoire ; la changer oblige chaque restaurant à ressaisir ses identifiants |
| `Payments:EnableSandbox` | Active l'intégration simulée `sandbox`. **Jamais en production** |
| `Payments:AllowSandboxOutsideDevelopment` | Autorise le sandbox hors développement (recette) |
| `Payments:SandboxCheckoutBaseUrl` | Page de paiement simulée renvoyée au client (facultatif) |

## Tests (`SaasRestaurant.Host.Tests`)

Le vrai `Program` en mode Production sur un vrai PostgreSQL (Testcontainers, Docker requis) ; seule la boîte mail
est remplacée, pour lire les liens. Les comptes sont créés et connectés par HTTP.
