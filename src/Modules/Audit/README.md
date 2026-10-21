# Module Audit — Domain, Application, Infrastructure, Api

But : tracer les actions sensibles sans permettre la modification de l'historique.

## 1. Frontière d'agrégat

- `AuditEntry` est sa propre racine de persistance (une ligne = une écriture), mais ce n'est **pas** un `AggregateRoot` :
  c'est une `Entity<AuditId>` append-only, sans mutateur ni événement de domaine. Hériter d'`AggregateRoot` aurait exposé
  `ClearDomainEvents()`, donc un état mutable, pour aucun bénéfice.
- Rien n'est référencé par navigation : tenant, acteur et cible sont des identifiants/valeurs opaques
  (`TenantId?`, `ActorId?`, `AuditTarget`). Le module ne connaît aucun autre module.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `AuditEntry` | Entity append-only | Écriture immuable : qui, quoi, sur quoi, quand, dans quel contexte | `(rien) -> créée` (aucune autre) | aucun | `common.id.empty` |
| `AuditId` | Id typé | Identité de l'écriture | — | — | `common.id.empty` |
| `ActorId` (+ `ActorType`) | Value Object | Référence opaque d'acteur : `User`, `ApiCredential`, `System` + identifiant | — | — | `audit.actor.type_invalid`, `audit.actor.value_invalid`, `common.id.empty` |
| `AuditAction` | Value Object | Code d'action normalisé `module.objet.fait` | — | — | `audit.action.invalid` |
| `AuditTarget` | Value Object | `TargetType` (code en minuscules) + `TargetId` (texte opaque, sans caractère de contrôle) | — | — | `audit.target.type_invalid`, `audit.target.id_invalid` |
| `CorrelationId` | Value Object | Lien avec la requête/trace d'origine | — | — | `audit.correlation_id.invalid` |
| `AuditMetadata` | Value Object | Contexte clé/valeur borné, en lecture seule, **expurgé des secrets par la clé et par la valeur** | — | — | `audit.metadata.*` |

## 3. Choix faits là où la spec était ouverte

- `TenantId` et `ActorId` sont optionnels (actions plateforme, requêtes anonymes). `AuditEntry` n'implémente donc pas `ITenantOwned`.
- `ActorId` est local au module (type + identifiant) pour permettre les acteurs non humains (`ActorId.ForSystem("nightly-job")`).
- `TargetType`/`TargetId` de la spec sont regroupés dans le VO `AuditTarget` ; `AuditEntry.TargetType` et `.TargetId` restent exposés.
- `OccurredAtUtc` vient de l'`IClock` à la création : l'écriture est faite dans la même transaction que l'action.
- `CorrelationId` est obligatoire (le service d'audit en génère un s'il n'y a pas de requête).
- Métadonnées : 50 entrées max, clé ≤ 100 caractères, valeur ≤ 1000 caractères, clés uniques sans tenir compte de la casse,
  valeur `null` refusée. Un dépassement est **rejeté** (pas tronqué en silence) : l'appelant sait ce qu'il écrit.
- `AuditTarget.Type` est normalisé en minuscules (`Order` et `order` sont **une** cible) ; `AuditTarget.Id` garde sa casse
  (id opaque). `AuditTarget.Id`, `AuditTarget.Type` et `ActorId.Value` refusent les caractères de contrôle et les séparateurs
  de ligne / paragraphe Unicode (U+2028, U+2029), comme `CorrelationId` : pas de ligne forgée dans un export du journal.
- `ActorId.Create(ActorType.User, value)` applique la même règle que `ForUser` : la valeur doit être un `Guid` non vide
  (`audit.actor.value_invalid` si ce n'est pas un Guid, `common.id.empty` s'il est vide) et est stockée sous la même forme
  canonique, donc `Create(User, "…")` et `ForUser(id)` sont égaux.
- Ids vides : `AuditEntry.Create` passe le `TenantId` (quand il est fourni) et `ActorId.ForUser` son `UserId` par
  `Guard.NotEmpty` du kernel. L'horloge est lue une seule fois (aucun événement).

### Expurgation des secrets : ce qui est expurgé

`AuditMetadata.Create` remplace la **valeur** par `[REDACTED]` (la clé est conservée) dès que la clé **ou** la valeur est
jugée sensible. L'expurgation passe avant le contrôle de longueur (un secret trop long est expurgé, pas rejeté).

**Par la clé** (`AuditMetadata.IsSensitiveKey`). La clé est d'abord normalisée : Unicode **FormKC** (les lettres pleine
chasse `ｐａｓｓｗｏｒｄ`, cerclées, etc. redeviennent des lettres simples), puis réduite à ses lettres et chiffres, en
minuscules — `Api-Key`, `api_key`, `apiKey`, `p.a.s.s.w.o.r.d`, `pass<zéro-largeur>word` donnent tous la même chose. Une clé
qui n'est pas de l'Unicode valide (surrogate isolé) est expurgée d'office.

- *Fragments cherchés n'importe où dans la clé normalisée* (`AuditMetadata.SensitiveKeyFragments`) : `password`, `passwd`,
  `passphrase`, `pwd`, `motdepasse`, `secret`, `token`, `bearer`, `jwt`, `authorization`, `auth`, `apikey`, `privatekey`,
  `key`, `credential`, `cookie`, `session`, `signature`, `connectionstring`, `iban`, `cardnumber`, `cvv`, `cvc`, `pincode`,
  `onetimepassword`.
- *Mots entiers de la clé* (`AuditMetadata.SensitiveKeyWords`) : `pin`, `otp`, `ssn`, `mdp`, `salt`. Ces cinq-là sont trop
  courts pour une recherche par sous-chaîne : `pin` est dans `shippingAddress`, `otp` dans `slotPosition`, `ssn` dans
  `businessName`, `mdp` dans `cmdParam`, `salt` dans `saltedCaramel`. La clé est donc découpée en mots (séparateurs, camelCase,
  sigles, chiffres finaux ignorés) : `pin`, `PIN`, `user_pin`, `pinCode`, `pin2`, `userSSN`, `password_salt` sont expurgés,
  `shippingAddress` ne l'est pas.
- *Sur-expurgation assumée* : `key` et `auth` sont larges **exprès** — `monkey`, `templateKey`, `author`, `authorId` sont
  expurgés ; de même `session` expurge `tableSessionId` et `token` expurge `tokenCount`. Un id utile à l'audit va dans
  `AuditTarget`, pas dans une métadonnée dont le nom contient un de ces fragments.

**Par la valeur** (`AuditMetadata.IsSensitiveValue`), quelle que soit la clé :

| Forme reconnue | Exemples expurgés |
|---|---|
| Identifiant HTTP : la valeur entière est `Bearer <jeton>` ou `Basic <jeton>` (casse ignorée, jeton ≥ 8 car.) | `Bearer abcdef123456`, `basic dXNlcjpwYXNzd29yZA==` |
| En-tête recopié : `authorization` suivi de `:`/`=` puis `Bearer`/`Basic`/`Digest`/`Negotiate` | `Authorization: Bearer abc`, une ligne `curl -H 'authorization: basic …'` |
| Bloc PEM : contient `-----BEGIN` | clé privée, certificat |
| JWT : trois segments base64url séparés par des points, le premier commençant par `eyJ`, n'importe où dans la valeur | jeton seul, ou au milieu d'une phrase / d'une URL |
| Identifiants dans une URL : `://utilisateur:motdepasse@` | `postgres://admin:hunter2@db:5432/app` |
| Paire nommée `nom=valeur`, `nom: valeur`, `"nom": valeur` (JSON, y compris échappé `\"nom\":`), où le nom contient `password`/`passwd`/`passphrase`, `mot de passe`, `secret`, `token`, `api_key`/`apikey`, `private_key`, `access_key`, `credential`, `cookie`, `signature`, `session_id`, `connection_string` | `…?token=abc`, `client_secret=…`, `Server=db;Password=x;`, `{"password":"x"}`, `mot de passe: 1234` |
| Paire à nom court, en nom entier : `pwd`, `pin`, `otp`, `mdp`, `jwt`, `ssn` (suivis de `=` ou `:`) ; `key`, `auth`, `sig` (suivis de `=`, ou entre guillemets JSON) | `pin=1234`, `otp: 123456`, `…?key=AIza…`, `{"key":"abc"}` |

Restent intacts (testés) : `Basic plan`, `Bearer of bad news`, `status: paid`, `authorization: granted by the manager`,
`The password policy was changed`, `token expired`, `shipping=express`, `monkey: 3`, une URL sans identifiants, un JSON sans
champ sensible, un Guid, une adresse IP, une heure. Sur-expurgation assumée côté valeur : une phrase comme
`token: expiré`, `secretary: Bob`, `cookie: 2,50` ou `Basic subscription` (un seul mot ≥ 8 car. après `Basic`) est expurgée.

### Limites honnêtes de l'expurgation (à connaître)

- Ce sont des **heuristiques**. Un secret rangé sous une clé anodine **et** dans une forme non reconnue passe toujours :
  `note = "le code est 1234"`, `ref = "hunter2"`, un jeton opaque nu (`sk_live_…`, clé hexadécimale) sans préfixe ni nom,
  un secret encodé (base64 d'un JSON), découpé sur deux entrées, ou nommé dans une autre langue que le français et l'anglais.
- La normalisation FormKC ne replie pas les homoglyphes d'autres alphabets (`раssword` avec des lettres cyrilliques).
- Les mots courts (`pin`, `otp`, `ssn`, `mdp`, `salt`) ne sont reconnus qu'en mot entier : `newpin` ou `userotp` (tout en
  minuscules, sans séparateur) passent.
- `Action`, `TargetId`, `CorrelationId` et `ActorId` ne sont pas filtrés : ne jamais y placer une donnée sensible.
- Le journal est append-only : un secret qui passe y reste. La vraie protection est donc **en amont** : l'Application
  construit les métadonnées à partir d'une **liste blanche de champs par action** (recommandé, voir §5) ; l'expurgation du
  domaine n'est que le filet de sécurité.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Append-only : aucun setter | `AuditEntry` (structure) | `AuditEntryTests.Entry_exposes_no_property_setter` |
| Append-only : champs readonly | `AuditEntry` (structure) | `AuditEntryTests.Entry_has_only_readonly_fields` |
| Append-only : aucune méthode d'instance | `AuditEntry` (structure) | `AuditEntryTests.Entry_declares_no_instance_method_besides_property_getters` |
| Création uniquement par la factory | `AuditEntry.Create` | `AuditEntryTests.Entry_can_only_be_built_through_its_factory` |
| Pas d'événement, pas d'état de racine mutable | `AuditEntry` | `AuditEntryTests.Entry_is_not_an_event_raising_aggregate` |
| VO de l'écriture immuables | VO | `AuditEntryTests.Value_objects_of_the_entry_are_immutable` |
| Dictionnaire de métadonnées non modifiable via le type exposé | `AuditMetadata.Values` | `AuditMetadataTests.Values_cannot_be_mutated_through_the_exposed_type` |
| Métadonnées indépendantes de la source | `AuditMetadata.Create` | `AuditMetadataTests.Values_are_a_copy_of_the_source` |
| Aucun secret : valeur des clés sensibles expurgée | `AuditMetadata.Create` | `AuditMetadataTests.Create_redacts_the_value_of_a_sensitive_key` |
| Seules les clés sensibles sont expurgées | `AuditMetadata.Create` | `AuditMetadataTests.Create_redacts_only_the_sensitive_keys`, `AuditMetadataTests.IsSensitiveKey_is_false_for_ordinary_keys`, `Harmless_keys_are_not_redacted` |
| Fragments étendus (`pwd`, `pin`, `otp`, `bearer`, `jwt`, `session`, `signature`, `auth`, `key`, `connectionstring`, `motdepasse`, `mdp`, `iban`, `ssn`, `salt`, `credential`, `cookie`) | `AuditMetadata.IsSensitiveKey` | `AuditMetadataTests.Create_redacts_every_listed_sensitive_key`, `Sensitive_key_lists_cover_every_reviewed_fragment` |
| Clé normalisée (FormKC, lettres/chiffres seulement) avant comparaison | `AuditMetadata.IsSensitiveKey` | `AuditMetadataTests.Create_redacts_width_and_separator_variants_of_a_sensitive_key`, `A_key_that_is_not_valid_unicode_is_treated_as_sensitive` |
| Sur-expurgation assumée de `key` / `auth` / `session` | `AuditMetadata.IsSensitiveKey` | `AuditMetadataTests.Broad_fragments_over_redact_on_purpose` |
| Listes de fragments non modifiables par transtypage | `SensitiveKeyFragments`, `SensitiveKeyWords` | `AuditMetadataTests.Sensitive_key_lists_cannot_be_modified_through_a_downcast` |
| Valeur `Bearer …` / `Basic …` expurgée | `AuditMetadata.IsSensitiveValue` | `AuditMetadataTests.Create_redacts_an_http_credential_stored_under_an_innocuous_key` |
| Valeur contenant `-----BEGIN` expurgée | `AuditMetadata.IsSensitiveValue` | `AuditMetadataTests.Create_redacts_a_pem_block` |
| Valeur contenant un JWT expurgée | `AuditMetadata.IsSensitiveValue` | `AuditMetadataTests.Create_redacts_a_json_web_token` |
| Valeur contenant une paire `password=` / `token=` / `secret=` / `api_key=` (query string, chaîne de connexion) | `AuditMetadata.IsSensitiveValue` | `AuditMetadataTests.Create_redacts_a_value_holding_a_named_secret_pair` |
| Valeur JSON contenant `"password":` etc. | `AuditMetadata.IsSensitiveValue` | `AuditMetadataTests.Create_redacts_serialized_json_holding_a_secret` |
| En-têtes recopiés et URL avec identifiants | `AuditMetadata.IsSensitiveValue` | `AuditMetadataTests.Create_redacts_logged_headers_and_urls_with_credentials` |
| Secret par la valeur expurgé même trop long | `AuditMetadata.Create` | `AuditMetadataTests.Create_redacts_a_secret_value_even_when_it_exceeds_the_length_limit` |
| Les valeurs inoffensives restent intactes | `AuditMetadata.IsSensitiveValue` | `AuditMetadataTests.Harmless_values_stay_intact` |
| Liste minimale de la spec couverte | `AuditMetadata.SensitiveKeyFragments` | `AuditMetadataTests.Sensitive_fragment_list_covers_the_minimum_required_by_the_spec` |
| Secret trop long expurgé (pas d'erreur) | `AuditMetadata.Create` | `AuditMetadataTests.Create_redacts_a_sensitive_value_even_when_it_exceeds_the_length_limit` |
| Aucun secret dans l'écriture | `AuditEntry.Create` | `AuditEntryTests.Create_never_stores_a_secret_passed_in_metadata`, `Create_never_stores_a_secret_hidden_under_an_innocuous_key` |
| Nombre d'entrées borné | `AuditMetadata.Create` | `AuditMetadataTests.Create_rejects_more_entries_than_the_limit` |
| Longueur de valeur bornée | `AuditMetadata.Create` | `AuditMetadataTests.Create_rejects_a_value_over_the_limit` |
| Clé non vide et bornée | `AuditMetadata.Create` | `AuditMetadataTests.Create_rejects_a_blank_key`, `Create_rejects_a_null_key`, `Create_rejects_a_key_over_the_limit` |
| Clés uniques (casse ignorée) | `AuditMetadata.Create` | `AuditMetadataTests.Create_rejects_duplicate_keys_ignoring_case` |
| Valeur non nulle | `AuditMetadata.Create` | `AuditMetadataTests.Create_rejects_a_null_value` |
| Tenant et acteur optionnels | `AuditEntry.Create` | `AuditEntryTests.Create_allows_a_platform_action_without_tenant_nor_actor` |
| Acteur système possible | `ActorId.ForSystem` | `AuditEntryTests.Create_allows_a_system_actor`, `AuditValueObjectsTests.ActorId_for_system_allows_a_non_human_actor` |
| TenantId fourni non vide | `AuditEntry.Create` | `AuditEntryTests.Create_rejects_a_default_tenant_id` |
| Action, cible, corrélation obligatoires | `AuditEntry.Create` | `AuditEntryTests.Create_requires_action_target_correlation_and_clock` |
| Tous les faits sont enregistrés, horodatage par l'horloge | `AuditEntry.Create` | `AuditEntryTests.Create_records_every_fact` |
| Format du code d'action | `AuditAction.Create` | `AuditValueObjectsTests.AuditAction_rejects_an_invalid_code`, `AuditAction_rejects_a_code_over_the_limit` |
| Format de la cible | `AuditTarget.Create` | `AuditValueObjectsTests.AuditTarget_rejects_an_invalid_type`, `AuditTarget_rejects_a_missing_id`, `AuditTarget_rejects_an_id_over_the_limit` |
| Type de cible en minuscules (une seule cible par type), id à la casse conservée | `AuditTarget.Create` | `AuditValueObjectsTests.AuditTarget_type_is_lower_cased_so_that_casing_variants_are_one_target`, `AuditTarget_id_keeps_its_casing` |
| Cible sans caractère de contrôle | `AuditTarget.Create` | `AuditValueObjectsTests.AuditTarget_rejects_control_characters_in_its_id`, `AuditTarget_rejects_control_characters_in_its_type` |
| Acteur valide | `ActorId.Create` | `AuditValueObjectsTests.ActorId_rejects_a_missing_value`, `ActorId_rejects_an_unknown_type`, `ActorId_for_user_rejects_a_default_user_id` |
| Acteur sans caractère de contrôle | `ActorId.Create`, `ForSystem` | `AuditValueObjectsTests.ActorId_rejects_control_characters` |
| Acteur `User` = Guid non vide, même forme que `ForUser` | `ActorId.Create` | `AuditValueObjectsTests.ActorId_created_for_a_user_is_the_same_as_ForUser`, `ActorId_created_for_a_user_rejects_anything_but_a_guid`, `ActorId_created_for_a_user_rejects_an_empty_guid` |
| Corrélation valide | `CorrelationId.Create` | `AuditValueObjectsTests.CorrelationId_rejects_an_invalid_value` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Service d'audit** (Application) : seul point d'écriture ; il fournit l'acteur, le tenant et le `CorrelationId` courants.
- **Append-only physique** : dépôt avec `Add` uniquement (ni `Update` ni `Delete`), droits SQL `INSERT`/`SELECT` seulement sur
  la table, éventuellement trigger/WORM. Le domaine garantit l'immutabilité de l'objet, pas celle de la ligne.
- Mapping ORM : propriétés get-only (champs de sauvegarde), `AuditMetadata` sérialisé (JSON) via son dictionnaire.
- Rétention/purge légale, pagination et recherche : read side.
- **Liste blanche des champs journalisés par action** (recommandée, voir « limites honnêtes de l'expurgation ») : chaque
  cas d'usage déclare les métadonnées qu'il a le droit d'écrire ; rien d'autre n'atteint `AuditMetadata.Create`.
- Les données existantes dont `TargetType` portait des majuscules sont à migrer en minuscules (une seule fois) pour que les
  recherches par cible restent cohérentes.

## 6. Persistance (étape 1)

Schéma `audit`, historique des migrations dans le même schéma. Une seule table, pas d'outbox (le journal ne publie rien).

| Colonne | Contenu |
|---|---|
| `id`, `tenant_id` (null : plateforme), `actor` (« User:… », « ApiCredential:… », « System:… »), `action`, `target` (« type/id »), `correlation_id`, `metadata` (jsonb, déjà expurgé), `occurred_at_utc`, `recorded_at_utc` | écrites une fois |
| `module`, `target_type`, `target_id` | CALCULÉES et stockées par PostgreSQL (recherche), jamais écrites par l'application |

Gardes en base : index unique `ix_audit_entries_origin` (corrélation, action, cible : un événement n'est journalisé
qu'une fois) ; index par restaurant et date, cible, acteur ; CHECK `occurred_at_utc <= recorded_at_utc`.

**Ajout seul physique** (`AuditDatabase.EnsureAppendOnlyAsync`, posé au démarrage après les migrations, idempotent) :
triggers qui refusent tout `UPDATE`, tout `DELETE` et tout `TRUNCATE` (erreur `42501`). Seule exception : la purge de
rétention, dans une transaction qui pose `SET LOCAL audit.allow_purge = 'on'`. Recommandé en production : donner au
compte applicatif les seuls droits `INSERT` et `SELECT` sur `audit.audit_entries` (et `DELETE` pour la purge).

Retouches du domaine : `AuditEntry.RecordedAtUtc` (instant d'écriture) et `Create(..., clock, occurredAtUtc)` (instant de
l'action quand l'écriture la suit, ramené à maintenant s'il est dans le futur) ; le constructeur vide « réservé à l'ORM »
est supprimé : EF Core reconstruit l'écriture par son constructeur complet (aucun setter, champs readonly).

Migration à générer :

```bash
dotnet ef migrations add InitialCreate --project src/Modules/Audit/Audit.Infrastructure --output-dir Persistence/Migrations
```

## 7. Cas d'usage, alimentation et API (étapes 2 à 6)

### Écriture : un seul point, alimenté par l'outbox

- `RecordAuditEntryCommand` (aucune route) : construit les objets-valeurs (le domaine valide et expurge), refuse un
  doublon d'origine (vérification puis index unique), écrit. Renvoie faux pour un rejeu.
- `AuditFeed` (hôte, consommateur d'outbox) : une RÈGLE par événement audité, avec code d'action, type et champ de la
  cible, auteur et LISTE BLANCHE des champs recopiés en métadonnées. Tenant = celui de l'événement. Origine =
  `outbox.{module}.{id du message}`. Instant = celui de l'événement.
- Événements audités (99) : Identity (comptes, mots de passe, verrouillages, sessions, rejeu de jeton) ; Staff (rôles,
  permissions, membres) ; Tenants (cycle de vie, réglages) ; Payments (remboursements, capture après annulation,
  prestataires, configurations et identifiants marchands) ; Billing (plans, abonnement) ; Inventory (ajustements,
  pertes, inventaires) ; Customers (anonymisation, consentements) ; Notifications (prestataires, configurations,
  effacement) ; Reporting (export demandé) ; Integrations (clés d'API, endpoints, secrets) ; Reviews (examen et archivage
  d'un avis, détachement de l'auteur, publication et clôture d'un questionnaire).
- Jamais recopiés : adresses e-mail, contacts, motifs saisis par un client, références de prestataire, secrets, URL
  complètes. Un test vérifie qu'aucune clé de métadonnée d'une règle n'est jugée sensible (elle serait expurgée).
- Auteur : le champ `Actor` de l'événement (Integrations), sinon l'utilisateur désigné par la règle (actions de
  l'utilisateur sur son compte, export demandé), sinon un traitement nommé (`webhook-delivery-job`), sinon inconnu.

### Lecture

| Cas d'usage | Qui | Règle |
|---|---|---|
| `SearchAuditEntries`, `GetAuditEntry` | Personnel (`audit.view`) | Tenant du CONTEXTE seulement ; une écriture d'un autre restaurant ou de la plateforme : 404 |
| `SearchPlatformAuditEntries`, `GetPlatformAuditEntry` | Administration de la plateforme | Tout le journal, un restaurant (`tenantId`), ou les écritures sans restaurant (`platformOnly`) |

Filtres : `module`, `action`, `targetType` + `targetId`, `actorType` + `actorId` (ensemble), `from` (inclus), `to`
(exclu), `skip`, `take` (100 au plus). Ordre : action la plus récente d'abord.

### Routes

| Route | Accès |
|---|---|
| `GET /audit`, `GET /audit/{id}` | `Tenancy.TenantMemberPolicy` + `audit.view` |
| `GET /admin/audit`, `GET /admin/audit/{id}` | `Policies.PlatformAdmin` |

Aucune route n'écrit, ne modifie ni ne supprime. `audit.view` n'est pas ouvert aux clés d'API (préfixe `audit.` exclu).

### Rétention

`AuditRetentionWorker` (toutes les 6 h) : purge par lots les écritures plus anciennes que `Audit:RetentionDays` (1095
jours par défaut ; 0 = conservation illimitée). Job coupable par `Audit:RunWorkers=false`.

## 8. Tests (étape 7)

| Projet | Contenu |
|---|---|
| `Audit.Domain.Tests` | + instant de l'action et d'enregistrement, instant futur ramené à maintenant |
| `Audit.Application.Tests` | Écriture complète, secret expurgé, rejeu, refus du domaine ; restaurant limité à son journal, 404 pour un autre ou pour la plateforme ; filtres ; filtres et pages invalides ; lecture de la plateforme |
| `Audit.Infrastructure.Tests` | Modèle (schéma, colonnes calculées, origine unique, pas de version) ; relecture par le domaine ; `UPDATE`, `DELETE`, `TRUNCATE` refusés EN BASE ; purge de rétention seule autorisée ; origine unique ; recherche par colonnes calculées |
| `Audit.Api.Tests` | Authentification, permission, isolation, 404, filtres, aucune route d'écriture (405), journal de la plateforme réservé |
| `SaasRestaurant.Host.Tests` | `AuditFeedTests` (règles, tenant, auteur, liste blanche, montants, clés sûres) ; création d'un restaurant journalisée de bout en bout |

## 9. Audit (étape 8)

| Point | Application | Vérification |
|---|---|---|
| Historique non modifiable | Domaine sans mutateur ; dépôt sans mise à jour ni suppression ; triggers en base | `AuditEntryTests`, `AuditPersistenceTests` |
| TenantId issu du contexte | Routes de restaurant filtrées sur le tenant du jeton ; écritures : tenant de l'événement | `AuditEndpointsTests`, `AuditUseCaseTests` |
| Aucun secret | Liste blanche par événement + expurgation du domaine | `AuditFeedTests`, `AuditUseCaseTests`, `AuditPersistenceTests` |
| Données personnelles | Ni e-mail ni contact ; ids seulement | `AuditFeedTests`, revue des règles |
| Idempotence | Origine unique (vérification + index) | `AuditUseCaseTests`, `AuditPersistenceTests` |
| Accès de la plateforme | Routes séparées, policy d'administration | `AuditEndpointsTests` |

Limites connues : la plupart des événements des autres modules ne portent pas leur auteur (seuls Integrations, les
actions d'un utilisateur sur son compte et les exports le donnent) : l'ajouter événement par événement (comme
`ChangeActor`) rendra le journal complet ; pas d'export du journal (CSV, SIEM) ; la purge est le seul moyen de
supprimer, y compris pour un effacement RGPD d'un utilisateur (ses ids restent, ils ne contiennent aucune donnée
personnelle) ; les triggers exigent PostgreSQL 14 au moins.
