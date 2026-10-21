# Module Customers — Domain

But : gérer le profil client, les consentements et les préférences sans surcollecte.

## 1. Frontières d'agrégat

| Racine | Contenu | Référencé par id seulement |
|---|---|---|
| `Customer` | `CustomerTag` (VO), `CustomerPreference` (VO), `ContactRequirementPolicy` (VO, épinglée), `Email` / `PhoneNumber` (kernel) | — |
| `Consent` | `ConsentChange` (VO, historique append-only), `ConsentSource` (VO), `WithdrawalReason` (enum) | `CustomerId` (kernel) |

- `Customer` porte le profil : il peut être modifié puis anonymisé.
- `Consent` est un agrégat séparé, **un par (client, canal)** : c'est une preuve légale qui doit survivre aux modifications du profil et être auditable seule. Il ne connaît le client que par `CustomerId` et reçoit un instantané `CustomerReference` (id, tenant, anonymisé ?) quand il doit décider.
- **Préférence ≠ consentement** : `CustomerPreference.MarketingChannels` exprime un *souhait* (« je préfère le SMS ») ; le droit d'envoyer vient uniquement d'un `Consent` au statut `Granted` sur ce canal. Les deux ne sont jamais déduits l'un de l'autre dans le domaine.
- Les autres modules (commandes, réservations, avis, notifications) ne référencent le client que par `CustomerId`. Quand ils doivent décider quelque chose à son sujet, ils reçoivent leur **propre** VO local, miroir de `CustomerReference` : `(TenantId, CustomerId, IsAnonymized)`, rempli par l'Application à partir de `Customer.ToReference()`. Aucun autre champ du client ne traverse la frontière.
- L'anonymisation se propage par l'événement `CustomerAnonymized` : ce module retire les consentements (`Consent.WithdrawBecauseCustomerAnonymized`), les autres effacent les copies qu'ils détiennent (voir §5).

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs (`customers.`) |
|---|---|---|---|---|---|
| `Customer` | Aggregate Root | Nom, contact, tags, préférences, anonymisation | `Active -> Anonymized` (terminal) | `CustomerCreated`, `CustomerContactChanged`, `CustomerContactRequirementChanged`, `CustomerAnonymized` | `customer.name_required`, `.name_too_long`, `.contact_required`, `.anonymized`, `.tag_already_present`, `.tag_not_found`, `.too_many_tags` |
| `ContactRequirementPolicy` | Value Object (policy) | Quels identifiants de contact sont exigés (`None`, `EmailOrPhone`, `Email`, `Phone`) | — | — | `contact_policy.requirement_unknown` |
| `CustomerTag` | Value Object | Étiquette normalisée (trim + minuscules), 1..30 car. | — | — | `tag.invalid` |
| `CustomerPreference` | Value Object | Langue (code type BCP-47) + ensemble de canaux marketing souhaités | — | — | `preference.language_invalid`, `.language_not_supported`, `.channel_not_supported` |
| `CustomerReference` | Value Object (snapshot) | Ce que `Consent` — et, par miroir, tout autre module — a le droit de savoir du client : `TenantId`, `CustomerId`, `IsAnonymized`. Constructeur `internal` : **uniquement** produit par `Customer.ToReference()`, donc infalsifiable | — | — | `common.id.empty` |
| `Consent` | Aggregate Root | Preuve de consentement marketing sur un canal, avec historique | `Granted -> Withdrawn -> Granted -> ...` | `ConsentGranted`, `ConsentWithdrawn` | `consent.not_unique`, `.channel_not_supported`, `.customer_anonymized`, `.customer_mismatch`, `.already_granted`, `.already_withdrawn`, `.reason_invalid`, `common.tenant.mismatch` |
| `ConsentSource` | Value Object | Origine de la collecte, sous forme de **code court** (`web-signup-form`, `pos`, `unsubscribe-link`) ; `ConsentSource.CustomerAnonymization` = source système | — | — | `consent.source_required`, `.source_too_long`, `.source_invalid` |
| `WithdrawalReason` | Enum | Motif fermé de retrait : `CustomerRequest`, `UnsubscribeLink`, `InvalidAddress`, `CustomerAnonymized`, `Other` | — | — | — |
| `ConsentChange` | Value Object | Ligne d'historique : statut atteint, date, source, motif (`null` pour un octroi) | — | — | — |

## 3. Choix faits là où la spec était ouverte

- **Contact « selon policy »** : la `ContactRequirementPolicy` (réglage du tenant) est passée à `Create` — défaut = au moins un email ou un téléphone, `None` permet un client de passage sans contact — puis **épinglée** sur le client (`Customer.ContactPolicy`). `UpdateContact` applique toujours la policy épinglée : un appelant ne peut plus l'assouplir le temps d'un appel. Elle ne change que par `ChangeContactRequirement(policy, clock)`, rejeté (`contact_required`) tant que le contact actuel ne satisfait pas la nouvelle policy ; event `CustomerContactRequirementChanged`.
- **`UpdateContact`** ne concerne qu'email et téléphone ; le nom se change par `Rename` (ajouté). L'événement `CustomerContactChanged` ne porte que des drapeaux (`EmailChanged`, `PhoneChanged`).
- **Aucune donnée personnelle dans les événements ni dans du texte libre** : ids, drapeaux et codes seulement. Le motif de retrait est l'enum `WithdrawalReason` (plus de texte libre, donc rien à effacer) ; `ConsentSource` est un code normalisé (trim + minuscules) à la grammaire `[a-z0-9]+([._-][a-z0-9]+)*`, ≤ 100 car. : il ne peut pas contenir une phrase, une adresse email ni un espace. `ConsentWithdrawn` porte le code source et le motif (enum).
- **Tags** : lettres, chiffres, espace, `-`, `_` ; uniques après normalisation ; 20 au maximum par client.
- **Préférences** : `null` tant que le client n'a rien exprimé. Langue au format `ll` ou `ll-RR`, normalisée (`fr-FR`), dont la langue primaire doit appartenir à `CustomerPreference.SupportedLanguages` (fr, en, es, pt, de, it, nl, ar ; `FrozenSet`, non modifiable même par transtypage). **Contrat inter-modules** : le module Notifications attend la seule sous-étiquette primaire (`fr`) ; l'Application la découpe avant de la lui passer. Les canaux forment un ensemble (doublons fusionnés, valeurs d'enum inconnues rejetées). Un canal préféré n'exige pas que le contact correspondant existe : ce n'est qu'un souhait.
- **Anonymisation** : irréversible ; efface email, téléphone, tags, préférences, remplace le nom par `Customer.AnonymizedName`, conserve `Id`, `TenantId`, `CreatedAtUtc`, pose `AnonymizedAtUtc`. Ensuite **tout** mutateur est rejeté avec `customers.customer.anonymized` (y compris une seconde anonymisation).
- **Consent** : `Consent.Grant(...)` (fabrique) crée l'agrégat au premier consentement ; `Regrant(...)` redonne un consentement retiré ; `Withdraw(source, reason, clock)` le retire. Donner deux fois ou retirer deux fois est rejeté. `GrantedAtUtc` / `Source` décrivent le dernier octroi et sont conservés après retrait ; `WithdrawnAtUtc` / `WithdrawalSource` / `WithdrawalReason` décrivent le retrait en cours et sont remis à `null` au ré-octroi ; `History` garde toutes les lignes, dans l'ordre.
- **Retrait traçable** : date (horloge), source obligatoire (code), motif obligatoire (enum ; `Other` quand rien ne convient).
- **Client anonymisé** : aucun octroi ni ré-octroi possible ; le retrait reste toujours possible (il ne prend pas de `CustomerReference`).
- **Le consentement suit l'anonymisation** : `Consent.WithdrawBecauseCustomerAnonymized(clock)` retire un consentement `Granted` avec la source système `ConsentSource.CustomerAnonymization` (`system.customer-anonymization`) et le motif `CustomerAnonymized`, et lève `ConsentWithdrawn`. Sur un consentement déjà retiré c'est un no-op silencieux (aucun event, le retrait d'origine est conservé) : l'appel est rejouable. Il n'a rien d'autre à effacer puisque l'agrégat et son historique ne contiennent plus aucun texte libre.
- **Même valeur = no-op silencieux** : `Rename` avec le même nom, `UpdateContact` avec le même contact, `UpdatePreferences` avec les mêmes préférences, `ChangeContactRequirement` avec la même policy ne modifient rien et ne lèvent aucun événement. Les **transitions** répétées restent rejetées : tag déjà présent / absent, double octroi, double retrait, seconde anonymisation.
- **Référence client infalsifiable** : `CustomerReference` décrit un agrégat de ce module ; son constructeur est `internal` et ses ids sont gardés (`Guard.NotEmpty`). On ne l'obtient que par `Customer.ToReference()` : ni l'Application ni un test ne peuvent fabriquer « un client non anonymisé du bon tenant » à partir d'ids libres. Sa forme documentée pour les miroirs locaux des autres modules reste `(TenantId, CustomerId, IsAnonymized)`.
- **Ids vides** : `Customer.Create` rejette un `TenantId` par défaut (`common.id.empty`) ; `Consent.Grant` ne reçoit ses ids que d'une `CustomerReference`, elle-même gardée.
- **Enums en entrée** : une valeur hors liste lève toujours une `DomainException` stable — `contact_policy.requirement_unknown` (au lieu d'une `ArgumentOutOfRangeException`), `preference.channel_not_supported`, `consent.channel_not_supported`, `consent.reason_invalid`.
- **Horloge lue une fois par opération** : `Anonymize` pose `AnonymizedAtUtc` et l'`OccurredAtUtc` de `CustomerAnonymized` avec le même instant.
- Pas d'unicité de l'email ou du téléphone par tenant : la spec ne la demande pas (voir remarques du rapport).

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Nom requis / borné | `Customer.Create`, `Rename` | `CustomerTests.Create_rejects_a_blank_name`, `Create_accepts_a_name_at_the_limit_and_rejects_a_longer_one`, `Rename_rejects_a_blank_or_a_too_long_name` |
| Même valeur = no-op silencieux | `Rename`, `UpdateContact`, `UpdatePreferences`, `ChangeContactRequirement` | `CustomerTests.Rename_to_the_current_name_is_a_silent_no_op`, `UpdateContact_with_the_current_contact_details_is_a_silent_no_op`, `UpdatePreferences_with_identical_preferences_is_a_silent_no_op`, `ChangeContactRequirement_to_the_current_policy_is_a_silent_no_op` |
| Au moins un contact (policy par défaut) | `Customer.Create`, `UpdateContact` | `CustomerTests.Create_with_the_default_policy_rejects_a_customer_without_any_contact`, `UpdateContact_rejects_removing_the_last_contact_under_the_default_policy` |
| Policy de contact explicite | `Customer.Create`, `ContactRequirementPolicy.IsSatisfiedBy` | `CustomerTests.Create_applies_the_explicit_contact_policy`, `CustomerValueObjectTests.Default_contact_policy_requires_an_email_or_a_phone`, `Contact_policy_checks_its_requirement` |
| Policy de contact épinglée, non assouplissable par appel | `Customer.Create`, `UpdateContact` | `CustomerTests.Create_pins_the_contact_policy`, `UpdateContact_applies_the_policy_pinned_at_creation`, `UpdateContact_cannot_be_given_another_policy` |
| Changement de policy validé contre le contact actuel | `ChangeContactRequirement` | `CustomerTests.ChangeContactRequirement_replaces_the_pinned_policy_and_raises_CustomerContactRequirementChanged`, `ChangeContactRequirement_rejects_a_policy_the_current_contact_does_not_satisfy`, `ChangeContactRequirement_can_relax_the_policy` |
| `CustomerCreated` à la création | `Customer.Create` | `CustomerTests.Create_builds_an_active_customer_and_raises_CustomerCreated` |
| `CustomerContactChanged` seulement si le contact change | `UpdateContact` | `CustomerTests.UpdateContact_replaces_the_contact_and_raises_CustomerContactChanged`, `UpdateContact_with_the_current_contact_details_is_a_silent_no_op` |
| Tag normalisé et valide | `CustomerTag.Create` | `CustomerValueObjectTests.Tag_is_trimmed_and_lower_cased`, `Tag_rejects_blank_or_forbidden_characters`, `Tag_accepts_the_maximum_length_and_rejects_a_longer_one` |
| Tags uniques | `AddTag` | `CustomerTests.AddTag_rejects_a_tag_already_present_whatever_its_casing` |
| Nombre de tags borné | `AddTag` | `CustomerTests.AddTag_rejects_more_tags_than_the_maximum` |
| Retrait d'un tag existant seulement | `RemoveTag` | `CustomerTests.RemoveTag_rejects_a_tag_the_customer_does_not_have` |
| Langue bien formée et supportée (test « validation ») | `CustomerPreference.Create` | `CustomerValueObjectTests.Preference_normalizes_a_supported_language`, `Preference_rejects_a_malformed_language`, `Preference_rejects_a_well_formed_but_unsupported_language` |
| Langues supportées non modifiables, même par transtypage | `CustomerPreference.SupportedLanguages` | `CustomerValueObjectTests.Supported_languages_cannot_be_modified_through_a_downcast` |
| Canaux marketing = ensemble de canaux connus | `CustomerPreference.Create` | `CustomerValueObjectTests.Preference_keeps_marketing_channels_as_a_set`, `Preference_rejects_an_unsupported_channel` |
| Anonymisation efface les données, garde id et tenant (test « anonymisation ») | `Anonymize` | `CustomerTests.Anonymize_erases_every_personal_datum_and_keeps_identity_and_tenant`, `Anonymize_raises_CustomerAnonymized_without_personal_data` |
| Anonymisation irréversible | `Anonymize` | `CustomerTests.Anonymize_is_irreversible_and_rejected_a_second_time` |
| Client anonymisé : aucun mutateur | `Rename`, `UpdateContact`, `ChangeContactRequirement`, `AddTag`, `RemoveTag`, `UpdatePreferences` | `CustomerTests.Anonymized_customer_rejects_Rename`, `Anonymized_customer_rejects_UpdateContact`, `Anonymized_customer_rejects_ChangeContactRequirement`, `Anonymized_customer_rejects_AddTag`, `Anonymized_customer_rejects_RemoveTag`, `Anonymized_customer_rejects_UpdatePreferences` |
| Référence exportable : tenant, id, anonymisé | `Customer.ToReference` | `CustomerTests.ToReference_carries_the_tenant_the_id_and_the_anonymized_flag`, `ToReference_reflects_the_anonymization` |
| Référence client infalsifiable (pas de constructeur ni de fabrique publics) | `CustomerReference` | `CustomerTests.A_CustomerReference_can_only_be_obtained_from_a_customer` |
| Tenant non vide | `Customer.Create` | `CustomerTests.Create_rejects_an_empty_tenant` |
| Exigence de contact = valeur d'enum connue | `ContactRequirementPolicy.Of` | `CustomerValueObjectTests.Contact_policy_is_a_value_and_rejects_an_unknown_requirement` |
| État et événement d'anonymisation au même instant | `Anonymize` | `CustomerTests.Anonymize_stamps_the_state_and_the_event_with_the_same_instant` |
| Octroi : date + source enregistrées | `Consent.Grant` | `ConsentTests.Grant_records_the_date_the_source_and_the_customer`, `Grant_raises_ConsentGranted` |
| Un consentement par (client, canal) | `Consent.Grant` | `ConsentTests.Grant_rejects_a_second_consent_for_the_same_customer_and_channel` |
| Pas d'octroi pour un client anonymisé | `Consent.Grant`, `Regrant` | `ConsentTests.Grant_rejects_an_anonymized_customer`, `Regrant_rejects_an_anonymized_customer` |
| Canal supporté | `Consent.Grant` | `ConsentTests.Grant_rejects_an_unsupported_channel` |
| Retrait traçable (test « withdraw ») | `Withdraw` | `ConsentTests.Withdraw_records_the_date_the_source_and_the_reason`, `Withdraw_appends_the_withdrawal_to_the_history`, `Withdraw_raises_ConsentWithdrawn` |
| Motif de retrait = liste fermée | `Withdraw` | `ConsentTests.Withdraw_accepts_every_known_reason`, `Withdraw_rejects_an_unknown_reason` |
| Aucun texte libre dans le consentement, son historique, son événement | `Consent`, `ConsentChange`, `ConsentWithdrawn` | `ConsentTests.Consent_and_its_history_hold_no_free_text`, `ConsentWithdrawn_carries_no_free_text` |
| Le consentement suit l'anonymisation (retrait si accordé, source système) | `WithdrawBecauseCustomerAnonymized` | `ConsentTests.WithdrawBecauseCustomerAnonymized_withdraws_a_granted_consent_with_the_system_source` |
| ... et reste idempotent (no-op sans event si déjà retiré) | `WithdrawBecauseCustomerAnonymized` | `ConsentTests.WithdrawBecauseCustomerAnonymized_is_a_silent_no_op_on_a_withdrawn_consent`, `WithdrawBecauseCustomerAnonymized_can_be_replayed` |
| Pas de double retrait | `Withdraw` | `ConsentTests.Withdraw_twice_is_rejected_and_keeps_the_first_withdrawal` |
| Pas de double octroi | `Regrant` | `ConsentTests.Regrant_is_rejected_while_the_consent_is_granted` |
| Ré-octroi après retrait, historique conservé | `Regrant` | `ConsentTests.Regrant_after_withdrawal_grants_again_with_a_new_date_and_source`, `Regrant_keeps_the_whole_history_traceable` |
| Ré-octroi pour le bon client, même tenant | `Regrant` | `ConsentTests.Regrant_rejects_another_customer`, `Regrant_rejects_a_customer_of_another_tenant` |
| Retrait jamais bloqué | `Withdraw` | `ConsentTests.Consent_of_an_anonymized_customer_can_still_be_withdrawn` |
| Source requise / bornée | `ConsentSource.Create` | `ConsentTests.Source_is_required`, `Source_accepts_the_maximum_length_and_rejects_a_longer_one` |
| Source = code court, jamais une phrase | `ConsentSource.Create` | `ConsentTests.Source_is_trimmed_and_lower_cased`, `Source_accepts_a_short_code`, `Source_rejects_anything_but_a_code`, `Source_of_the_anonymization_is_a_valid_code` |
| Historique non modifiable de l'extérieur | `Consent.History` | `ConsentTests.History_cannot_be_modified_from_outside` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Unicité du consentement** : port `IConsentUniquenessChecker` ; index unique `(TenantId, CustomerId, Channel)` en base comme garde-fou final. L'Application charge le `Consent` existant et appelle `Regrant` / `Withdraw`, ou `Consent.Grant` s'il n'existe pas.
- **Anonymisation de bout en bout** : sur `CustomerAnonymized`, l'Application charge **tous** les consentements du client (accordés ou non) et appelle `WithdrawBecauseCustomerAnonymized` sur chacun (idempotent, donc rejouable). Les autres modules réagissent au même événement pour effacer ce qu'ils ont copié : Reviews (`Feedback.DetachCustomer`), Notifications (`Cancel` des envois en attente puis `Redact`), etc. L'Application purge aussi les copies hors domaine (index de recherche, caches, exports).
- **Référence client pour les autres modules** : l'Application charge le `Customer` (sous isolation tenant), lit `Customer.ToReference()` et remplit le VO local du module appelant `(TenantId, CustomerId, IsAnonymized)`. Elle ne peut pas construire elle-même une `CustomerReference` de ce module.
- **Avant tout envoi marketing** : vérifier `Consent.IsGranted` sur le canal (la préférence ne suffit pas), et que le contact existe.
- **Concurrence** : version optimiste sur `Customer` et `Consent`.
- **Policy de contact** : lue par l'Application dans le réglage du tenant et passée à `Customer.Create` uniquement. Quand le tenant change son réglage, l'Application appelle `ChangeContactRequirement` sur les clients concernés et traite ceux qui le rejettent (`contact_required` : contact à compléter d'abord).
- **Codes de source** : l'Application fournit des codes stables (`pos`, `web-signup-form`...), jamais une saisie utilisateur.
- **Conservation** : durée de rétention des preuves de consentement, droit d'accès / export.

## 6. Application (`Customers.Application`)

Le tenant d'un cas d'usage vient **toujours** d'`ITenantContext.RequireTenant()` (claim `tid` en requête,
`BackgroundTenant` pour un message traité en arrière-plan). Une écriture exige un tenant qui accepte les écritures
(`customers.tenant.not_writable`), SAUF le retrait d'un consentement et l'anonymisation : un désabonnement et le droit
à l'effacement ne dépendent pas de l'abonnement du restaurant.

| Cas d'usage | Effet | Erreurs propres à l'Application |
|---|---|---|
| `CreateCustomer(name, email?, phone?, tags?, preferences?)` | Fiche active ; règle de contact = réglage du restaurant au moment de la création, figée sur la fiche ; tags normalisés (doublons fusionnés) | — |
| `RenameCustomer`, `UpdateCustomerContact(email, phone)`, `AddCustomerTag`, `RemoveCustomerTag`, `UpdateCustomerPreferences` | Modifications par l'agrégat (contact vérifié selon la règle figée) | `customers.customer.not_found` |
| `ApplyContactPolicy(customerId)` | Applique le réglage ACTUEL du restaurant à une fiche (refusé tant que son contact ne le satisfait pas) | idem |
| `AnonymizeCustomer(customerId)` | Efface les données personnelles ET retire chaque consentement accordé, dans la même transaction (source système, motif `CustomerAnonymized`) | idem |
| `GrantConsent(customerId, channel, source?)` | Premier octroi (crée la preuve) ou nouvel octroi après retrait ; source = code court, `back-office` par défaut | idem |
| `WithdrawConsent(customerId, channel, reason, source?)` | Retrait daté, source et motif enregistrés | `customers.consent.not_found` |
| `SetContactPolicy(requirement)` | Réglage du restaurant (`customer_settings`) ; ne modifie pas les fiches existantes | — |
| `GetCustomerById`, `SearchCustomers(name?, email?, phone?, tag?, includeAnonymized, skip, take ≤ 100)`, `ListCustomerConsents`, `GetContactPolicy` | Lectures, possibles pour un restaurant suspendu ; recherche : nom CONTENU (sans casse, jokers pris à la lettre), email et téléphone EXACTS après normalisation, tag ; anonymisés exclus par défaut | `customers.search.page_invalid` |
| `GetMarketingPermission(customerId, channel)` | Envoi marketing permis ? Consentement accordé ET adresse du canal (email ; téléphone pour SMS et WhatsApp) ; jamais la préférence. Motif du refus : `anonymized`, `no_consent`, `consent_withdrawn`, `no_contact` | — |
| `GetCustomerReference(customerId)` | Pour l'hôte seulement (pas de route) : `(tenantId, customerId, isAnonymized)` et RIEN d'autre | — |

- **Doublons** : aucune unicité de l'email ni du téléphone (choix du domaine) ; la recherche exacte sert à retrouver une
  fiche avant d'en créer une.
- **Permissions** : les fiches sont des données personnelles, la LECTURE est donc protégée : `customers.view` (lecture,
  recherche, consentements), `customers.manage` (fiches, tags, préférences, consentements), `customers.anonymize`
  (effacement), `customers.configure` (règle de contact).
- **Port fourni par l'hôte** : `ICustomersTenants`.

## 7. Infrastructure (`Customers.Infrastructure`)

- Schéma `customers` : `customers` (nom normalisé en majuscules pour la recherche et le tri, index non uniques sur
  `(tenant_id, email)` et `(tenant_id, phone)`, préférences en jsonb), `customer_tags` (clé client + valeur),
  `consents` (index unique tenant + client + canal), `consent_changes` (historique en insertion seule, identité
  croissante), `customer_settings` (une ligne par restaurant), `outbox_messages`.
- Contraintes CHECK (valeurs générées depuis les enums) : statut, règle de contact, canal, statut et motif d'un
  consentement ; un retrait a sa date, sa source et son motif, un octroi n'en a aucun ; **une fiche anonymisée ne garde
  ni email, ni téléphone, ni préférences** (`ck_customers_anonymized_has_no_personal_data`).
- **Concurrence** : version `xmin` sur les fiches, consentements et réglages ; un changement de tag réécrit la fiche (deux
  ajouts simultanés ne dépassent pas le maximum) ; deux octrois simultanés sur le même canal : l'index unique refuse le
  second (`customers.consent.not_unique`).
- **Migration** : `dotnet ef migrations add InitialCreate --project src/Modules/Customers/Customers.Infrastructure --startup-project src/Modules/Customers/Customers.Infrastructure --output-dir Persistence/Migrations` (`database update` lit `CUSTOMERS_DB`).

## 8. API (`Customers.Api`) et hôte

| Méthode | Route | Corps | Permission |
|---|---|---|---|
| `GET` / `POST` | `/customers` (`?name&email&phone&tag&includeAnonymized&skip&take`) | `{ name, email?, phone?, tags?, preferences? }` -> `{ id }` | `customers.view` / `customers.manage` |
| `GET` | `/customers/{id}` | — | `customers.view` |
| `PUT` | `/customers/{id}/name` · `/contact` · `/preferences` | `{ name }` · `{ email, phone }` · `{ language, marketingChannels? }` | `customers.manage` |
| `POST` · `DELETE` | `/customers/{id}/tags` · `/customers/{id}/tags?tag=` | `{ tag }` | `customers.manage` |
| `POST` | `/customers/{id}/contact-policy/apply` | — | `customers.manage` |
| `POST` | `/customers/{id}/anonymize` | — | `customers.anonymize` |
| `GET` | `/customers/{id}/consents` · `/consents/{channel}/marketing-permission` | — | `customers.view` |
| `POST` | `/customers/{id}/consents/grant` · `/withdraw` | `{ channel, source? }` · `{ channel, reason?, source? }` | `customers.manage` |
| `GET` / `PUT` | `/customers/settings/contact-policy` | `{ requirement }` | `customers.view` / `customers.configure` |

- **Hôte** (`CustomersBridges.cs`) : Tenants ; `CustomerReferences` sert `IOrderCustomers` (Orders) et
  `IReservationCustomers` (Reservations) par `GetCustomerReferenceQuery` : une commande et une réservation peuvent être
  rattachées à une fiche (`customerId` facultatif ; fiche anonymisée refusée, fiche d'un autre restaurant introuvable).
  `CustomerAnonymizationFlow` : `customers.CustomerAnonymized` -> `AnonymizeCustomerReservations` (contact et notes de
  TOUTES les réservations du client, idempotent) par l'outbox.
- **Hors périmètre pour l'instant** : notes libres sur la fiche (données sensibles à cadrer), fusion de doublons, export
  des données d'un client (droit d'accès), durée de conservation des preuves, effacement des copies dans Reviews et
  Notifications (modules pas encore construits : ils consommeront le même événement).
