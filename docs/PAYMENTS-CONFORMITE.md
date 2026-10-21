# Module Payments — conformité au cahier des charges

État au 09/10/2026, après l'étape 7 (configurations EF Core). Légende : **OK** conforme ;
**PARTIEL** existe mais incomplet ; **MANQUE** absent ; **ÉCART** fait autrement, volontairement (à valider).

## Synthèse

| § | Exigence | État | Commentaire |
|---|---|---|---|
| 1 | Niveau plateforme : `PaymentProvider`, `PaymentProviderCapability`, `PaymentProviderCode`, `PaymentProviderType`, `PaymentMethodType` | OK (étape 1) | Nom, code unique, type, pays, devises, moyens, remboursement (partiel), autorisation/capture, webhook, récurrence, expiration, min/max par devise, statut |
| 2 | Agrégat `RestaurantPaymentConfiguration` | OK (étape 2) | TenantId, PaymentProviderId, Provider, ProviderType, `PaymentMode`, `PaymentConfigurationStatus`, IsEnabled, `SecretReference`, moyens retenus, affectations, dates. `Create`, `Configure`, `ChangeMode`, `Enable`/`Disable`, `Activate/DeactivatePaymentMethod`, `AssignToLocation`/`RemoveFromLocation`, `CanTake` |
| 3 | `PaymentMode` PlatformManaged / TenantManaged | OK (étape 2) | PlatformManaged seulement si la capacité `SupportsPlatformManagedMode` l'autorise (jamais sur place) ; compte de la plateforme saisi par l'admin (`PUT /platform/payment-providers/{id}/credentials`) |
| 4 | Secrets hors agrégats, `IPaymentSecretStore`, `PaymentCredentialReference` | OK (étape 2) | Coffre PostgreSQL chiffré AES-256-GCM dans `payment_secrets`, la référence authentifiée ; les agrégats, événements, réponses et logs ne portent que la référence (ou rien). Vault : même interface |
| 5 | `PaymentMethodAssignment` par établissement | PARTIEL (étape 2) | Domaine, persistance et tests faits (tenant étranger refusé, priorité, min/max). MANQUENT : endpoints et port vers Locations (avec la policy de disponibilité) |
| 6 | Capabilities, pas de `if provider == X` | OK | Aucun test sur un nom de prestataire dans le métier ; seule la nature (sur place / en ligne) intervient |
| 7 | `GetAvailablePaymentMethods` + `PaymentMethodAvailabilityService` | OK (P2, 14/10/2026) | `PaymentMethodAvailability` (domaine) et `GetAvailablePaymentMethodsQuery` : établissement, montant, devise, pays et canal (comptoir ou en ligne) ; tri par nature, priorité puis nom. Route `GET /payments/payment-methods/available`, utilisée par la caisse et le site |
| 8 | Agrégat `Payment` | OK (étape 3) | `LocationId` (celui de la commande), `PaymentMethodType`, `AuthorizedAtUtc`, `FailedAtUtc` ajoutés ; `Create` / `RecordOffline` partent de la `RestaurantPaymentConfiguration` : même tenant, configuration active et prête, moyen accepté, établissement affecté, bornes min/max. Statut de la plateforme et capacités : Application (puis policy, étape 13) |
| 9 | `PaymentAttempt` | OK (étape 4) | `FailureCode` (`PaymentFailureCode`, normalisé depuis le code ou le motif du prestataire) + motif facultatif borné ; aucune donnée bancaire ; tentative terminée jamais rouverte |
| 10 | `Refund` | OK (étape 4) | `ProviderReference?` (unique par paiement, index filtré), `CompletedAtUtc` (ex-`ProcessedAtUtc`) ; montant nul/négatif, supérieur au remboursable et double traitement refusés |
| 11 | `IPaymentGateway` | PARTIEL | Démarrer / annuler / rembourser ; pas de `CaptureAsync` (capture confirmée par webhook) ni de résultats typés `PaymentGatewayResult` |
| 12 | `IPaymentGatewayResolver` | OK (autre nom) | `IPaymentIntegrations.CreateGateway(code, identifiants)` : dictionnaire par code, aucun `switch` |
| 13 | Webhooks génériques | OK / ÉCART | Les 9 étapes sont couvertes (signature, anti-rejeu, idempotence, file, application en arrière-plan, outbox). ÉCART : adresse `/payments/webhooks/{provider}/{configurationId}` (un secret par compte marchand) au lieu de `/webhooks/{provider}` ; table renommée `provider_webhook_events` (étape 6) |
| 14 | `Idempotency-Key` (création, capture, remboursement) | MANQUE | Seuls les webhooks sont idempotents. Deux demandes de remboursement identiques créent deux remboursements |
| 15 | Domain Events | OK (autre nommage) | Convention de la solution : sans suffixe `DomainEvent` (`PaymentCaptured`...) |
| 16 | `Payments.Contracts` + Integration Events via outbox → Kafka | MANQUE | L'outbox existe ; les consommateurs lisent le JSON des événements de domaine dans le même processus. Ni projet Contracts, ni Kafka dans la solution |
| 17 | Arborescence `RestaurantSaaS.Payments.*` | ÉCART | La solution nomme ses projets `<Module>.<Couche>` (`Payments.Domain`...), comme les 20 autres modules |
| 18 | Tables PostgreSQL | OK / ÉCART (étape 6) | Schéma `payments` : `payment_providers`, `restaurant_payment_configurations`, `payment_method_assignments`, `payments`, `payment_attempts`, `refunds`, `provider_webhook_events`, `payment_secrets`, `outbox_messages` ; `tenant_id` sur TOUTES les tables d'un restaurant (enfants compris) ; contraintes CHECK (montants, devises, statuts et moyens générés depuis les enums, priorité, référence de coffre du MÊME tenant, propriétaire d'une ligne du coffre) (étape 7). ÉCART : capacités en jsonb dans `payment_providers` (pas de table `payment_provider_capabilities`) ; `idempotency_records` à l'étape 18 |
| 19 | Endpoints admin SaaS | PARTIEL | `/platform/payment-providers` (GET, POST, PUT, enable, disable, integrations, credentials). ÉCART de préfixe validé : pas de `/api/v1/admin` |
| 20 | Endpoints restaurant | PARTIEL | `/payments/providers`, `/payments/configurations` (+ credentials, mode, methods, enable, disable), `/payments/payment-methods/available` (P2). MANQUE : `/locations` ; pas de préfixe `/api/v1` (validé) |
| 21 | Tests obligatoires | PARTIEL | Voir le détail ci-dessous |
| 22 | Dépendances entre couches et modules | OK / MANQUE | Vérifié par les tests d'architecture ; pas de projet Contracts |
| 23 | Sécurité | PARTIEL | Voir le détail ci-dessous |

## Tests obligatoires (§21)

| Test | État |
|---|---|
| A ne lit pas la configuration de B / ne la modifie pas | OK (Application + API) |
| Provider désactivé par la plateforme indisponible | OK |
| Provider désactivé par le restaurant indisponible | OK |
| Provider incompatible avec la devise | OK |
| Provider incompatible avec le pays | PARTIEL : capacité testée (étape 1), pas encore la résolution |
| Provider non assigné à l'établissement | OK (Domain + Application à l'encaissement) ; résolution `GetAvailablePaymentMethods` à l'étape 13 |
| Montant < minimum / > maximum rejeté | OK pour les bornes de l'établissement (Domain + Application) ; bornes des capacités à la résolution (étape 13) |
| Remboursement > capturé rejeté | OK (Domain) |
| Double webhook sans double effet | OK (Domain + Infrastructure) |
| Double capture rejetée ou idempotente | OK (Domain) |
| Double remboursement protégé | PARTIEL : double issue protégée ; double DEMANDE non (pas d'`Idempotency-Key`) |
| Credentials jamais retournés par l'API | OK (Application + API : ni valeur, ni référence) |
| Credentials jamais dans les logs | MANQUE |
| Location d'un autre tenant rejetée | OK (Domain) ; à refaire au niveau API avec les endpoints d'affectation |
| Payment terminal non modifiable | OK (Domain : matrice `PaymentStateMachineTests`, `Terminal_payments_keep_their_money_untouched_whatever_is_tried`) |
| Nouveau `IPaymentGateway` sans toucher au Domain | OK : le domaine ne nomme aucun prestataire réel (`PaymentDomainGuardTests`) ; intégration `testpay` des tests Infrastructure sans changement du domaine |

## Sécurité (§23)

| Mesure | État |
|---|---|
| Secrets chiffrés / externalisés | OK : coffre à part derrière `IPaymentSecretStore` (PostgreSQL chiffré ; Vault possible) |
| HTTPS obligatoire | MANQUE dans l'hôte (`UseHttpsRedirection` / HSTS absents) ; à faire au niveau du proxy ou de l'hôte |
| Signatures webhook, anti-rejeu | OK (HMAC, horodatage ±5 min, dédoublonnage par id d'événement) |
| Idempotence | PARTIEL (webhooks seulement) |
| Rate limiting | MANQUE sur `/payments` et les webhooks (seul `/auth` est limité) |
| Audit | MANQUE (module Audit pas encore construit) |
| CorrelationId, TenantId, logs structurés | OK (portée de log par requête) |
| Aucune donnée bancaire, tokenisation par le prestataire | OK (aucun numéro de carte ne transite) |
| Aucune clé dans les logs / Kafka | OK dans le code, sans test |
| Validation stricte montants et devises | OK (`Money` : précision de la devise, bornes) |

## Décisions validées pour l'étape 2

1. **Secrets (§4)** : remplacer les identifiants chiffrés de l'agrégat par une `PaymentCredentialReference`
   (`payments/tenant/{tenantId}/{provider}`) et un `IPaymentSecretStore`. Proposition : une implémentation PostgreSQL
   chiffrée (table séparée, hors agrégats) par défaut, puis Vault / un gestionnaire de secrets du cloud derrière la même
   interface.
2. **PlatformManaged (§3)** : le prévoir dans le modèle (mode + identifiants de la plateforme par prestataire), sans
   l'activer pour un prestataire qui ne l'autorise pas (capacité « encaissement pour compte de tiers »).
3. **Par établissement (§5)** : ajouter `PaymentMethodAssignment` (établissement, moyen, actif, priorité, min/max),
   ce qui revient sur le choix « configuration au niveau du restaurant seulement ».
4. **Préfixe `/api/v1`** et nommage `RestaurantSaaS.*` : garder la convention actuelle de la solution (recommandé) ou
   tout migrer.
5. **Contracts + Kafka (§16)** : créer `Payments.Contracts` et les Integration Events dès maintenant (publiés par
   l'outbox aux consommateurs internes), Kafka venant plus tard derrière le dispatcher.

## Étape 1 livrée

- `PaymentProviderCode` (ex-`ProviderCode`), `PaymentProviderType` (`OnSite` / `Online`), `PaymentMethodType`
  (carte, Apple Pay, Google Pay, PayPal, mobile money, virement, espèces, carte sur place, mobile money sur place,
  titre-restaurant) avec la compatibilité moyen / nature, `CountryCode`, `PaymentAmountLimit`,
  `PaymentProviderCapability` (pays, devises, moyens, autorisation, remboursement, remboursement partiel, webhook,
  récurrence, expiration, limites par devise).
- `PaymentProvider` : `Register`, `Rename`, `ChangeCapabilities`, `Enable`, `Disable`, `CanTakePaymentsIn` ;
  événements `PaymentProviderRegistered`, `...Renamed`, `...CapabilitiesChanged`, `...Enabled`, `...Disabled` ;
  erreurs `payments.provider.*`.
- Tests : `PaymentProviderTests` (11 tests). Couches Application, Infrastructure et API adaptées pour que tout compile
  (capacités en jsonb, saisies par l'admin plateforme).
## Étape 2 livrée

- Domaine : `RestaurantPaymentConfiguration` (remplace `RestaurantPaymentMethod`), `PaymentMethodAssignment`,
  `PaymentMode`, `PaymentConfigurationStatus`, `PaymentCredentialReference`, `LocationReference`,
  `PaymentConfigurationErrors` (`payments.configuration.*`), 9 événements sans secret ; capacité
  `SupportsPlatformManagedMode`. `ProtectedCredentials` supprimé.
- Application : `IPaymentSecretStore` (remplace `IPaymentCredentialsProtector`), `IRestaurantPaymentConfigurationRepository` ;
  commandes `SetPlatformCredentials`, `CreatePaymentConfiguration`, `ConfigurePaymentConfiguration`, `ChangePaymentMode`,
  `Enable/DisablePaymentConfiguration`, `SetPaymentMethodActive` ; requête `ListPaymentConfigurations`.
- Infrastructure : `PostgresPaymentSecretStore` + `SecretCipher` (table `payment_secrets`), mapping des configurations
  et des affectations, webhooks et relais passant par le coffre.
- API : `/payments/configurations/*`, `PUT /platform/payment-providers/{id}/credentials`, webhooks
  `/payments/webhooks/{provider}/{configurationId}`.
- Tests : `PaymentConfigurationTests` (14), `PaymentConfigurationUseCaseTests` (10), persistance (coffre chiffré, copie
  de chiffré refusée, affectations), API (aucun secret ni référence en sortie).

## Étape 3 livrée

- `Payment` : `LocationId`, `Method` (`PaymentMethodType`), `AuthorizedAtUtc`, `FailedAtUtc` ; `OrderReference` porte
  le `LocationId` de la commande ; `Create(configuration, method, order, amount, clock)` et
  `RecordOffline(configuration, method, order, amount, reference, clock)` vérifient la configuration
  (`RestaurantPaymentConfiguration.EnsureCanTake`) et la nature du prestataire ; `PaymentCreated` porte l'établissement
  et le moyen. Nommage des événements sans suffixe `DomainEvent` (convention de la solution).
- Application : `PaymentOrder.LocationId` (fourni par l'hôte depuis Orders), `paymentMethod` facultatif s'il n'y a
  qu'un moyen (`payments.method.payment_method_required` sinon), domaine vérifié AVANT l'appel au prestataire, nouvel
  essai revérifié. API : `/payments/offline` prend `provider` (ex-`method`) et `paymentMethod?`.
- Tests : `PaymentCreationRulesTests` (9), cas d'usage (4), persistance (1) ; tests existants adaptés.

## Étape 4 livrée

- `PaymentFailureCode` (`Create` strict, `FromProvider` tolérant, `ProviderDeclined`, `PaymentCancelled`) ;
  `Payment.Fail(reference, code, clock, reason?)` ; `PaymentFailed` porte le code.
- `Refund.ProviderReference`, `CompletedAtUtc` ; `Payment.RegisterRefund(id, issue, clock, reference?)` refuse une
  référence déjà prise et une issue rejouée contradictoire ; `RefundRegistered` / `RefundSucceeded` portent la référence.
- Application : `ApplyProviderResultCommand` accepte `FailureCode` et `RefundReference` (les lecteurs de webhooks les
  rempliront à l'étape 17) ; DTO : `failureCode`, `refunds[].providerReference`, `refunds[].completedAtUtc`
  (ex-`processedAtUtc`).
- Base : `payment_attempts.failure_code`, `payment_refunds.provider_reference` (index unique filtré),
  `payment_refunds.completed_at_utc` (ex-`processed_at_utc`).
- Tests : `AttemptAndRefundDetailsTests` (18 cas), cas d'usage (1), persistance (code relu) ; tests existants adaptés.

## Étape 5 livrée : tests du domaine

Couverture des invariants du cahier (§1 à §10) par les tests `Payments.Domain` :

| Invariant | Tests |
|---|---|
| Catalogue : code unique, nature, capacités cohérentes, limites | `PaymentProviderTests` |
| Configuration : modes, identifiants par référence, moyens, affectations, tenant étranger refusé | `PaymentConfigurationTests` |
| Paiement : montant > 0, devise, reste à payer, tenant, configuration (active, prête, moyen, établissement, bornes) | `PaymentFlowTests`, `PaymentCreationRulesTests`, `PaymentHardeningTests` |
| Transitions contrôlées, état terminal protégé, rejeux sans effet, refus sans effet de bord | `PaymentStateMachineTests` (42 cas : 7 statuts × 6 opérations), `PaymentWebhookIdempotenceTests` |
| Aucun changement direct du statut | test d'architecture `Entities_have_no_public_setter`, `PaymentFlowTests.Payment_and_its_children_expose_no_public_setter` |
| Tentative terminée jamais rouverte, code d'échec | `ChildEntityTests`, `AttemptAndRefundDetailsTests` |
| Remboursement : nul/négatif, > capturé, remboursé en totalité, double traitement, référence | `PaymentRefundTests`, `ChildEntityTests`, `AttemptAndRefundDetailsTests` |
| Capture tardive après annulation | `PaymentLateCaptureTests` |
| Aucun prestataire réel nommé, aucun secret ni donnée bancaire dans les événements et l'agrégat | `PaymentDomainGuardTests`, `PaymentConfigurationTests.Configuration_events_carry_no_secret`, `PaymentProviderTests.Provider_events_carry_no_secret_...` |

Ajouts de l'étape : `PaymentStateMachineTests` (43 cas), `PaymentDomainGuardTests` (4). Aucun code de production modifié.

## Étape 6 livrée : `PaymentsDbContext`

- Tables renommées comme au §18 : `payment_refunds` -> `refunds`, `webhook_inbox` -> `provider_webhook_events`.
- `tenant_id` ajouté aux tables enfants (`payment_attempts`, `refunds`, `payment_method_assignments` : propriété cachée
  recopiée de la racine par l'unit of work) et à `payment_secrets` (NULL pour la plateforme) ; index de rapprochement des
  webhooks sur `(tenant_id, provider_reference)`.
- Contraintes CHECK : montants positifs (`payments`, `refunds`), capturé non négatif, statuts, mode, priorité 0..1000,
  référence de coffre obligatoire pour une configuration en ligne prête en mode restaurant.
- Tests (Infrastructure) : tables présentes, `tenant_id` sur chaque ligne enfant et chaque secret, montant négatif refusé
  par la base.
- Capacités gardées en une valeur jsonb (objet valeur remplacé en bloc, jamais interrogé ligne à ligne) plutôt qu'une
  table `payment_provider_capabilities` : retenu par défaut à l'étape 7 (sans objection).

- **La migration Payments est à régénérer** pour lancer les tests Infrastructure et API (colonne `capabilities`,
  colonnes `type`). Le schéma changera encore aux étapes suivantes : elle sera régénérée une dernière fois avant la mise
  en service.

## Étape 7 livrée : configurations EF Core

- Organisation : l'ancien fichier unique `PaymentsConfigurations.cs` est découpé par agrégat dans
  `Persistence/Configurations/` : `PaymentsConverters.cs` (conversions, `Checks`, `ChildTenant`),
  `PaymentConfigurations.cs` (paiement, tentatives, remboursements), `PaymentProviderConfiguration.cs`,
  `RestaurantPaymentConfigurations.cs` (configuration, affectations), `InfrastructureTableConfigurations.cs` (coffre, file
  des webhooks).
- CORRECTION : `SupportsPlatformManagedMode` n'était pas écrit dans le jsonb des capacités ; un prestataire autorisant le
  mode plateforme le perdait au rechargement. Noms des champs JSON désormais fixés par attribut (camelCase) ; lecture
  insensible à la casse pour les lignes déjà écrites.
- CHECK des enums GÉNÉRÉS depuis l'enum (`Checks.In<T>`) : statuts, moyens, nature du prestataire, mode, type de
  résultat d'un webhook. Ajouter une valeur à un enum change le modèle, donc la migration.
- Nouvelles contraintes : devise ISO (`^[A-Z]{3}$`) des montants, capture dans la devise du paiement, `jsonb_typeof` des
  colonnes jsonb, `attempts >= 0` ; SÉCURITÉ : la référence au coffre d'une configuration est celle de SON tenant
  (`ck_restaurant_payment_configurations_secret_reference_tenant`) et une ligne du coffre appartient au tenant de sa
  référence, ou à personne pour la plateforme (`ck_payment_secrets_owner`).
- Index : file des webhooks en index PARTIEL (`processed_at_utc IS NULL`) ; `payment_method_assignments (tenant_id,
  location_id)` pour la résolution des moyens (étape 13) ; index de clé étrangère des tentatives nommé selon la convention.
- Tests : `PaymentsModelTests` (sans base) : migration à jour, schéma et noms snake_case, `tenant_id` obligatoire, texte
  borné, précision 19,3, CHECK exact de chaque enum, aucune colonne de secret ou de donnée bancaire, version `xmin` des
  agrégats, enfants sans `DbSet` et en cascade. `PaymentsPersistenceTests` : capacités relues à l'identique, lignes
  invalides refusées par la base (5 cas), référence au coffre d'un autre restaurant ou de la plateforme refusée, ligne du
  coffre recopiée sous un autre propriétaire refusée.
- **La migration Payments est à régénérer** (contraintes et index nouveaux) ; `The_migration_matches_the_model` échoue
  tant que ce n'est pas fait.
