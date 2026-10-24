# Module Notifications — Domain

But : gérer l'intention d'envoi et son cycle de vie sans dépendre du fournisseur.

## 1. Frontière d'agrégat

> Les §1 à §5 décrivent la première version du domaine. Pour `Notification`, `NotificationRecipient` et
> `NotificationContent`, le §10 (étape 7) fait foi ; pour les tentatives (`NotificationDelivery`, ex-`NotificationAttempt`),
> le §11 (étape 8) : statuts, méthodes et paramètres y ont changé.

- `Notification` : un message à délivrer à un destinataire sur un canal. Enfants : `NotificationAttempt` (tentatives),
  créées et clôturées uniquement par la racine (factory et mutateurs `internal`).
- `NotificationTemplate` : **une version** d'un template `(tenant?, Key, Channel, Language, Version)`. Chaque version est sa propre racine :
  une version publiée est figée, la suivante est un autre agrégat. Pas d'agrégat « famille de versions » (il n'aurait aucun
  invariant à protéger qu'un index unique ne protège pas déjà).
- Une notification référence son template par `TemplateKey` + `Language` uniquement (pas de navigation, pas de version : le
  moteur de rendu prend la dernière version publiée au moment de l'envoi, selon la règle de résolution du §3).
- **Niveau plateforme** : `TenantId` est **nullable** sur les deux racines. `null` = message / template de la plateforme
  elle-même (vérification de compte, réinitialisation de mot de passe : envoyés avant qu'un tenant existe), comme le tenant
  optionnel d'Audit. Les deux racines n'implémentent donc **pas** `ITenantOwned` (le contrat du kernel exige un `TenantId`
  non nul) ; elles exposent `TenantId? TenantId` et `IsPlatformLevel`. L'isolation par tenant de l'Infrastructure doit
  traiter ces deux tables explicitement (§5).
- Le consentement vit dans le module Customers : il arrive ici sous forme de `ConsentProof` (petit VO local), jamais par dépendance.
  Le client n'est connu que par le `CustomerId` du kernel.
- Le fournisseur (SMTP, SMS, push) est en Infrastructure.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `Notification` | AggregateRoot (tenant **ou** plateforme) | Cycle de vie de l'envoi, tentatives, retry borné, effacement | `Pending -> Sent` (`MarkSending` puis `MarkSent`) ; `Pending -> Failed` (`MarkSending` puis `RegisterFailure`) ; `Failed -> Pending` (`ScheduleRetry`) ; `Pending/Failed -> Cancelled` ; `Sent` et `Cancelled` terminaux ; `Redact` (ne change pas le statut) depuis `Sent`, `Cancelled` ou `Failed` sans tentative restante | `NotificationCreated`, `NotificationSent`, `NotificationFailed`, `NotificationDeliveryAbandoned`, `NotificationRetryScheduled`, `NotificationCancelled`, `NotificationRedacted` | `notifications.notification.*` |
| `NotificationDelivery` (ex-`NotificationAttempt`) | Entity enfant | Une tentative réelle : prestataire, id du message chez lui, issue, échec, relance (voir §11) | `InProgress -> Sent -> PartiallyDelivered -> Delivered` ; `InProgress/Sent -> Failed` | (levés par la racine) | `notifications.delivery.*`, `notifications.failure.*` |
| `NotificationRecipient` (ex-`Recipient`) | Value Object | Canal (`NotificationChannel`, InApp compris) + adresse cohérente avec le canal ; forme « effacée » (`IsRedacted`, adresse vide) produite uniquement par `Notification.Redact` | — | — | `common.email.*`, `common.phone.invalid`, `notifications.recipient.device_token_invalid`, `.channel_invalid` |
| `NotificationLanguage` | Value Object | Sous-étiquette de langue primaire, 2-3 lettres minuscules (`fr`, `en`) | — | — | `notifications.language.invalid` |
| `NotificationPayload` | Value Object | Variables validées (≤ 50, nom contrôlé ≤ 64, valeur non nulle ≤ 2000) | — | — | `notifications.payload.*` |
| `NotificationPurpose` | Enum | `Transactional`, `Marketing` | — | — | — |
| `ConsentProof` | Value Object | Réponse de Customers : consentement marketing accordé ou non, pour **un client** et un canal | — | — | `notifications.recipient.channel_invalid`, `common.id.empty` |
| `NotificationRetryPolicy` | Value Object | Nombre max de tentatives + backoff exponentiel plafonné (plafond ≤ 7 jours), épinglé sur la notification | — | — | `notifications.retry_policy.invalid` |
| `NotificationTemplate` | AggregateRoot (tenant **ou** plateforme) | Contenu versionné d'un template, par langue | `Draft -> Published` ; `Draft -> Archived` ; `Published -> Archived` ; `Archived` terminal ; `Published/Archived --CreateNextVersion--> nouveau Draft (Version + 1)` | `TemplatePublished`, `TemplateArchived` | `notifications.template.*` |
| `TemplateKey` | Value Object | Clé fonctionnelle normalisée (`reservation.confirmed`) | — | — | `notifications.template_key.invalid` |
| `ITemplateUniquenessChecker` | Port | Unicité `(tenant?, key, channel, language[, version])` ; tenant `null` = templates plateforme | — | — | — |
| `NotificationId`, `NotificationDeliveryId`, `TemplateId` | Ids typés | — | — | — | `common.id.empty` |

## 3. Choix faits là où la spec était ouverte

**Notification**
- `Channel` = `CommunicationChannel` du kernel, porté par `Recipient` (`Notification.Channel` en est dérivé) : l'adresse ne peut
  pas être incohérente avec le canal. Email -> `Email` du kernel ; Sms/WhatsApp -> `PhoneNumber` (E.164) ; Push -> jeton opaque
  (non vide, sans espace, ≤ 512).
- **Deux fabriques, jamais de tenant nullable en paramètre** : `Notification.Create(tenantId, ...)` (tenant obligatoire,
  `common.id.empty` sinon) et `Notification.CreateForPlatform(...)` (sans tenant). Un appel tenant ne peut donc jamais devenir
  « plateforme » par un `null` passé par erreur. Une notification plateforme est **toujours `Transactional`** et sans client :
  `CreateForPlatform` n'a ni paramètre de finalité, ni client, ni consentement — le marketing y est impossible par construction
  (un consentement n'existe que dans un tenant). Tous les events portent `TenantId?`.
- **Langue** : `Notification.Language` (obligatoire, `NotificationLanguage`) est la langue pour laquelle le message a été
  créé ; le moteur de rendu choisit le template de cette langue. **Contrat inter-modules** : Customers stocke la préférence
  en `ll` ou `ll-RR` ; l'Application passe la sous-étiquette primaire (`fr-FR` -> `fr`). `fr-FR` est rejeté ici
  (`notifications.language.invalid`), la casse est normalisée.
- **Client** : `Notification.CustomerId` (kernel, optionnel) ; `null` pour un destinataire membre du staff, plateforme ou
  invité. C'est la clé par laquelle l'Application retrouve les notifications d'un client à effacer.
- **Consentement** : `Create` reçoit un `NotificationPurpose`. `Transactional` : aucun consentement requis (une preuve fournie
  est ignorée). `Marketing` : `CustomerId` obligatoire (`customer_required`), preuve obligatoire (`consent_required`), obtenue
  pour **ce client** (`consent_customer_mismatch`), pour le **même canal** que le destinataire (`consent_channel_mismatch`) et
  accordée (`consent_not_granted`).
- **Re-vérification à l'envoi** : `MarkSending(clock, consent)` exige pour une notification `Marketing` une preuve **fraîche**
  et applique exactement les mêmes contrôles ; un consentement retiré depuis la création arrête donc un message en attente
  ou en retry (`consent_not_granted`), sans ouvrir de tentative. La notification reste `Pending` : l'Application l'annule.
  Pour une notification `Transactional` le paramètre est ignoré.
- **Pas de statut « Sending »** : la spec liste `MarkSending` mais seulement les statuts Pending/Sent/Failed/Cancelled. La
  tentative en vol est donc modélisée par la dernière `NotificationAttempt` à l'état `InProgress`, la notification restant
  `Pending`. `MarkSending` ouvre la tentative, `MarkSent`/`RegisterFailure` la clôturent. Deux tentatives en vol simultanées
  sont interdites (`attempt_in_progress`).
- **Retry** : `RegisterFailure` -> `Failed` + `NotificationFailed` (levé à chaque tentative échouée). `ScheduleRetry(clock)`
  n'est permis que depuis `Failed` et tant qu'il reste des tentatives ; il repasse en `Pending` avec
  `NextAttemptAtUtc = now + backoff`. Retries épuisés -> rejet (`retries_exhausted`), la notification reste `Failed`
  (non terminal : elle peut encore être annulée). `MarkSending` avant l'échéance est rejeté (`not_due`).
- **Borne de retry épinglée** : la `NotificationRetryPolicy` est fixée à la création (paramètre optionnel des fabriques,
  `Default` sinon : 3 tentatives, 1 min doublée, plafond 30 min) et stockée dans `Notification.RetryPolicy`. `ScheduleRetry`
  et `CanRetry` (propriété) ne prennent plus de policy : un appelant ne peut plus rallonger la borne. `MaxAttempts` compte la
  première tentative. `MaxBackoff` est borné à 7 jours (`NotificationRetryPolicy.MaxAllowedBackoff`) : le doublement ne peut
  pas déborder.
- **Abandon observable** : quand l'échec enregistré est celui de la dernière tentative permise, `RegisterFailure` lève, après
  `NotificationFailed`, l'event métier `NotificationDeliveryAbandoned` (`IsDeliveryAbandoned` devient vrai). Le statut reste
  `Failed`.
- **Aucune donnée personnelle dans les events** : ids, canal, clé de template, compteurs. `NotificationFailed` ne porte plus
  le message du fournisseur (il cite souvent l'adresse du destinataire et un event émis ne s'efface pas) ; il reste lisible
  sur l'agrégat (`LastFailureReason`, `NotificationAttempt.FailureReason`) jusqu'à l'effacement.
- **Effacement** : `Redact(clock)` remplace `Recipient` par le destinataire effacé (même canal, adresse vide — valeur qu'aucun
  destinataire réel ne peut avoir, donc `Recipient.IsRedacted` est sans ambiguïté et `Notification.Channel` reste exact),
  remplace `Payload` par `NotificationPayload.Empty`, efface `LastFailureReason` et la raison d'échec de chaque tentative, pose
  `RedactedAtUtc` (`IsRedacted`) et lève `NotificationRedacted` (ids seulement). Statut, dates, tentatives, `TemplateKey`,
  `Language`, `CustomerId` et `TenantId` sont conservés. Permis uniquement quand plus aucun envoi n'est possible : `Sent`,
  `Cancelled`, ou `Failed` **sans tentative restante** (`IsDeliveryAbandoned`). Sinon rejet `not_redactable` : une
  notification `Pending` (tentative en vol ou non) ou `Failed` avec des tentatives restantes doit d'abord être annulée
  (et la tentative en vol d'abord clôturée). Idempotent : un second `Redact` est un no-op sans event.
- `Cancel` : depuis `Pending` ou `Failed`, mais pas pendant une tentative en vol (le fournisseur a peut-être déjà le message :
  il faut d'abord enregistrer l'issue).
- Events ajoutés : `NotificationRetryScheduled`, `NotificationCancelled`.
- Le payload est validé en forme (noms, tailles, pas de null), pas contre les variables attendues par le template : cette
  vérification a besoin du template rendu et se fait à l'envoi.

**NotificationTemplate**
- Identité fonctionnelle d'une version : `Key + Channel + Language + Version`, par tenant ou au niveau plateforme. `Create`
  (tenant) et `CreatePlatformTemplate` (sans tenant) produisent la version 1 en `Draft` ; `CreateNextVersion` conserve tenant,
  clé, canal et langue. Les events `TemplatePublished` / `TemplateArchived` portent `TenantId?` et la langue.
- **Règle de résolution** (appliquée par le moteur de rendu, en Infrastructure) pour une notification
  `(tenant, key, channel, language)` : 1. la plus haute version **publiée** du template du tenant pour
  `key + channel + language` ; 2. à défaut, la plus haute version publiée du template **plateforme** pour le même triplet.
  Une notification plateforme ne regarde que l'étape 2. Les templates « système » n'ont donc pas à être copiés dans chaque
  tenant : il suffit qu'ils existent au niveau plateforme ; un tenant les surcharge en publiant le sien. Pas de repli entre
  langues dans le domaine : l'Application choisit la langue de la notification (langue du client, sinon langue du tenant).
- **Même valeur = no-op silencieux** : `EditDraft` avec le sujet et le corps actuels ne modifie rien et ne touche pas
  `UpdatedAtUtc` (les contrôles d'état et de contenu restent appliqués avant).
- `Draft` éditable (`EditDraft`), `Published` **immuable** (`published_immutable` sur `EditDraft` et sur un second `Publish`).
- Pour modifier un template publié : `CreateNextVersion` renvoie un nouveau `Draft` `Version + 1`, copie du contenu ; la version
  source n'est pas touchée et continue de servir tant que la nouvelle n'est pas publiée. Interdit depuis un `Draft` (il est
  encore éditable), permis depuis `Published` ou `Archived`.
- **Statut `Archived` ajouté** : la spec liste la méthode `Archive` mais seulement les statuts Draft/Published. `Archive` est
  permis depuis `Draft` (abandon) et `Published` (retrait) ; `Archived` est terminal. Event `TemplateArchived` ajouté.
- **Subject** : obligatoire pour Email (≤ 200, trimé), **interdit** pour Sms/WhatsApp/Push (`subject_not_supported`) plutôt
  qu'ignoré en silence ; une chaîne vide/blanche y est assimilée à « pas de sujet ».
- Body : obligatoire, ≤ 20 000 caractères, conservé tel quel (pas de trim). La syntaxe des variables n'est pas interprétée.
- **Durcissement après revue (G1..G7)** :
  - *Ids vides* : le tenant est **optionnel par conception** ; les fabriques tenant (`Notification.Create`,
    `NotificationTemplate.Create`) passent leur `TenantId` — et le `CustomerId?` quand il est présent — par `Guard.NotEmpty`
    du kernel (`common.id.empty`), les fabriques plateforme (`CreateForPlatform`, `CreatePlatformTemplate`) n'en prennent pas.
  - *Références* : `ConsentProof` et `Recipient` décrivent des données d'autres modules (Customers, contact) ; ils restent
    constructibles publiquement par l'Application, `ConsentProof` garde son `CustomerId`. Aucun VO de ce module ne décrit un
    agrégat du module lui-même, donc rien à rendre `internal`.
  - *Enums* : `purpose`, `channel` hors liste lèvent `purpose_invalid` / `channel_invalid` (jamais d'`ArgumentOutOfRangeException`).
  - *Horloge lue une fois par opération* : `Create`, `MarkSending`, `MarkSent`, `RegisterFailure`, `ScheduleRetry`, `Cancel`,
    `Redact`, `Publish`, `Archive` posent dates d'état, dates de tentative et `OccurredAtUtc` avec le même instant.
  - *Pas de collection statique publique mutable* ; `NotificationPayload.Variables` est un `ReadOnlyDictionary` sur une copie privée.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Adresse cohérente avec le canal | `Recipient.Create` | `NotificationValueObjectsTests.Recipient_rejects_a_phone_number_on_the_email_channel`, `Recipient_rejects_an_email_address_on_a_phone_channel`, `Recipient_rejects_an_invalid_device_token`, `Recipient_rejects_an_unknown_channel` |
| Canal pris du destinataire | `Notification.Create` | `NotificationTests.Create_takes_the_channel_from_the_recipient` |
| Variables validées (nom, doublon, null, tailles, nombre) | `NotificationPayload.Create` | `NotificationValueObjectsTests.Payload_rejects_an_invalid_variable_name`, `Payload_rejects_a_duplicated_variable`, `Payload_rejects_a_null_value`, `Payload_value_length_is_bounded`, `Payload_variable_count_is_bounded`, `Payload_variable_name_length_is_bounded` |
| Payload immuable | `NotificationPayload` | `NotificationValueObjectsTests.Payload_is_immutable_and_compared_by_value` |
| Variable : pas de saut de ligne final accepté (`\z`) | `NotificationPayload.Create` | `NotificationValueObjectsTests.Payload_rejects_a_variable_name_holding_a_line_break` |
| Pas d'envoi marketing sans preuve de consentement | `Notification.Create` | `NotificationTests.Create_marketing_without_consent_proof_is_rejected` |
| Marketing : client obligatoire | `Notification.Create` | `NotificationTests.Create_marketing_without_customer_is_rejected` |
| Marketing : preuve obtenue pour ce client | `Notification.Create` | `NotificationTests.Create_marketing_with_the_consent_of_another_customer_is_rejected` |
| Preuve = client + canal + décision | `ConsentProof` | `NotificationValueObjectsTests.ConsentProof_carries_customer_channel_and_decision`, `ConsentProof_rejects_an_unknown_channel`, `ConsentProof_rejects_a_default_customer` |
| Consentement re-vérifié à l'envoi (marketing) | `MarkSending` | `NotificationTests.MarkSending_marketing_with_a_fresh_granted_consent_opens_the_attempt`, `MarkSending_marketing_is_stopped_by_a_consent_withdrawn_since_creation`, `MarkSending_marketing_requires_a_fresh_proof_for_the_same_customer_and_channel`, `A_retried_marketing_notification_is_stopped_by_a_withdrawn_consent` |
| Transactionnel : consentement ignoré à l'envoi | `MarkSending` | `NotificationTests.MarkSending_transactional_ignores_the_consent` |
| Notification plateforme : sans tenant, sans client, transactionnelle | `Notification.CreateForPlatform` | `NotificationTests.CreateForPlatform_builds_a_transactional_notification_without_tenant_nor_customer`, `A_platform_notification_can_never_be_marketing` |
| Un appel tenant ne devient jamais plateforme | `Notification.Create`, `NotificationTemplate.Create` | `NotificationTests.A_tenant_call_cannot_silently_become_platform_level`, `Create_rejects_a_default_tenant`, `NotificationTemplateTests.A_tenant_call_cannot_silently_become_platform_level` |
| Même cycle de vie au niveau plateforme, events avec `TenantId` nul | toutes | `NotificationTests.A_platform_notification_follows_the_same_lifecycle_and_its_events_carry_no_tenant`, `A_platform_notification_can_be_cancelled` |
| Langue = sous-étiquette primaire | `NotificationLanguage.Create` | `NotificationValueObjectsTests.Language_is_a_normalized_primary_subtag`, `Language_rejects_anything_but_a_primary_subtag`, `Language_is_compared_by_value` |
| Pas d'envoi marketing si consentement refusé | `Notification.Create` | `NotificationTests.Create_marketing_with_denied_consent_is_rejected` |
| Consentement pour le bon canal | `Notification.Create` | `NotificationTests.Create_marketing_with_consent_for_another_channel_is_rejected` |
| Marketing avec consentement accepté ; transactionnel sans consentement | `Notification.Create` | `NotificationTests.Create_marketing_with_granted_consent_is_accepted`, `Create_transactional_needs_no_consent_and_ignores_a_denied_one` |
| Création -> `Pending` + `NotificationCreated` | `Notification.Create` | `NotificationTests.Create_builds_a_pending_notification_and_raises_NotificationCreated` |
| Tenant obligatoire, finalité connue, client non vide | `Notification.Create` | `NotificationTests.Create_rejects_a_default_tenant`, `Create_rejects_an_unknown_purpose`, `Create_rejects_a_default_customer` |
| `MarkSending` ouvre une tentative sans changer le statut | `MarkSending` | `NotificationTests.MarkSending_opens_an_attempt_without_changing_the_status` |
| Pas deux tentatives en vol | `MarkSending` | `NotificationTests.MarkSending_rejects_a_second_concurrent_attempt` |
| Pas d'envoi d'une notification `Failed` sans retry planifié | `MarkSending` | `NotificationTests.MarkSending_rejects_a_failed_notification_until_a_retry_is_scheduled` |
| Pas d'envoi avant l'échéance du retry | `MarkSending` | `NotificationTests.MarkSending_is_rejected_before_the_retry_is_due` |
| Succès -> `Sent` + `NotificationSent` | `MarkSent` | `NotificationTests.MarkSent_closes_the_attempt_and_raises_NotificationSent` |
| Succès/échec exigent une tentative en vol | `MarkSent`, `RegisterFailure` | `NotificationTests.MarkSent_requires_an_attempt_in_progress`, `RegisterFailure_requires_an_attempt_in_progress` |
| Échec -> `Failed` + `NotificationFailed` | `RegisterFailure` | `NotificationTests.RegisterFailure_closes_the_attempt_and_raises_NotificationFailed` |
| Raison d'échec requise et bornée | `RegisterFailure` | `NotificationTests.RegisterFailure_requires_a_reason`, `RegisterFailure_rejects_a_reason_over_the_limit` |
| **Retry** : `Failed -> Pending` avec échéance | `ScheduleRetry` | `NotificationTests.ScheduleRetry_requeues_a_failed_notification_after_the_backoff`, `A_retry_opens_a_second_attempt_which_can_succeed`, `Retry_backoff_grows_with_each_failed_attempt` |
| Retry seulement depuis `Failed` | `ScheduleRetry` | `NotificationTests.ScheduleRetry_is_rejected_when_the_notification_did_not_fail` |
| **Retry borné** : épuisé = rejeté | `ScheduleRetry`, `CanRetry` | `NotificationTests.ScheduleRetry_is_rejected_once_the_attempts_are_exhausted`, `CanRetry_reflects_the_pinned_policy_and_the_status` |
| Borne de retry épinglée à la création | `Notification.Create`, `ScheduleRetry` | `NotificationTests.Retry_policy_is_pinned_at_creation_and_cannot_be_given_per_call`, `Create_builds_a_pending_notification_and_raises_NotificationCreated` |
| Dernier échec permis -> `NotificationDeliveryAbandoned` | `RegisterFailure` | `NotificationTests.RegisterFailure_on_the_last_allowed_attempt_raises_NotificationDeliveryAbandoned`, `A_failure_with_attempts_left_is_not_an_abandonment`, `An_abandoned_notification_can_still_be_cancelled_but_never_sent` |
| Pas de message fournisseur dans les events | `NotificationFailed` | `NotificationTests.NotificationFailed_carries_no_provider_message` |
| Politique de retry valide, backoff plafonné | `NotificationRetryPolicy` | `NotificationValueObjectsTests.Retry_policy_rejects_invalid_settings`, `Retry_policy_backoff_doubles_up_to_the_cap`, `Retry_policy_is_compared_by_value_and_knows_exhaustion` |
| Plafond de backoff ≤ 7 jours, pas de débordement | `NotificationRetryPolicy` | `NotificationValueObjectsTests.Retry_policy_backoff_is_capped_at_seven_days_and_never_overflows` |
| Annulation depuis `Pending`/`Failed`, pas pendant une tentative | `Cancel` | `NotificationTests.Cancel_stops_a_pending_notification_and_raises_NotificationCancelled`, `Cancel_stops_a_failed_notification`, `Cancel_is_rejected_while_an_attempt_is_in_progress` |
| **Terminal** `Sent` | toutes | `NotificationTests.Sent_is_terminal` |
| **Terminal** `Cancelled` | toutes | `NotificationTests.Cancelled_is_terminal` |
| **Effacement** : destinataire, payload, raisons d'échec effacés ; le reste conservé | `Redact` | `NotificationTests.Redact_erases_the_recipient_the_payload_and_the_failure_reasons_of_a_sent_notification` |
| `NotificationRedacted` avec ids seulement | `Redact` | `NotificationTests.Redact_raises_NotificationRedacted_with_ids_only` |
| Effacement permis depuis `Sent`, `Cancelled`, `Failed` sans tentative restante | `Redact` | `NotificationTests.Redact_erases_the_recipient_the_payload_and_the_failure_reasons_of_a_sent_notification`, `Redact_is_allowed_on_a_cancelled_notification`, `Redact_is_allowed_once_the_delivery_is_abandoned` |
| Effacement rejeté tant qu'un envoi reste possible ; annuler d'abord | `Redact`, `Cancel` | `NotificationTests.Redact_is_rejected_while_the_notification_can_still_be_sent`, `A_pending_notification_is_redacted_after_being_cancelled`, `A_failed_notification_with_attempts_left_is_redacted_after_being_cancelled` |
| Effacement idempotent, état final conservé | `Redact` | `NotificationTests.Redact_twice_is_a_silent_no_op`, `A_redacted_notification_stays_in_its_final_state` |
| Un destinataire effacé ne se construit pas de l'extérieur | `Recipient` | `NotificationValueObjectsTests.Recipient_built_from_outside_is_never_redacted` |
| Tentatives non modifiables de l'extérieur, pas de setter public | structure | `NotificationTests.Attempts_cannot_be_mutated_from_outside` |
| Template créé en `Draft` version 1 | `NotificationTemplate.Create` | `NotificationTemplateTests.Create_builds_version_one_as_a_draft` |
| Clé unique par (tenant, canal, langue) | `NotificationTemplate.Create` | `NotificationTemplateTests.Create_rejects_a_key_already_used_for_the_channel_and_language`, `Templates_of_different_languages_share_a_key_and_a_channel` |
| Template plateforme : sans tenant, mêmes règles, même cycle de vie | `CreatePlatformTemplate` | `NotificationTemplateTests.CreatePlatformTemplate_builds_version_one_as_a_draft_without_tenant`, `CreatePlatformTemplate_applies_the_same_rules_as_a_tenant_template`, `A_platform_template_follows_the_same_lifecycle_and_its_events_carry_no_tenant` |
| Subject requis pour Email, borné | `Create`, `EditDraft` | `NotificationTemplateTests.Create_requires_a_subject_for_email`, `Create_bounds_the_subject_length` |
| Subject interdit hors Email | `Create`, `EditDraft` | `NotificationTemplateTests.Create_forbids_a_subject_outside_email` |
| Body requis et borné | `Create`, `EditDraft` | `NotificationTemplateTests.Create_requires_a_body`, `Create_bounds_the_body_length` |
| Canal connu, tenant obligatoire | `Create` | `NotificationTemplateTests.Create_rejects_an_unknown_channel`, `Create_rejects_a_default_tenant` |
| `Draft` éditable, mêmes règles de contenu | `EditDraft` | `NotificationTemplateTests.EditDraft_changes_the_content_of_a_draft`, `EditDraft_applies_the_same_content_rules_and_keeps_the_previous_content` |
| Même contenu = no-op silencieux | `EditDraft` | `NotificationTemplateTests.EditDraft_with_the_current_content_is_a_silent_no_op` |
| Publication -> `TemplatePublished` | `Publish` | `NotificationTemplateTests.Publish_freezes_the_draft_and_raises_TemplatePublished` |
| **Template publié immuable** | `EditDraft`, `Publish` | `NotificationTemplateTests.A_published_template_is_immutable` |
| **Versioning** : nouvelle version pour modifier (tenant, clé, canal, langue conservés) | `CreateNextVersion` | `NotificationTemplateTests.Versioning_a_published_template_is_changed_through_a_new_draft_version`, `CreateNextVersion_starts_as_a_draft_copy_of_the_source`, `CreateNextVersion_increments_from_the_source_version` |
| Pas de nouvelle version depuis un `Draft` | `CreateNextVersion` | `NotificationTemplateTests.CreateNextVersion_is_rejected_from_a_draft` |
| Version suivante unique | `CreateNextVersion` | `NotificationTemplateTests.CreateNextVersion_is_rejected_when_that_version_already_exists` |
| Nouvelle version possible depuis `Archived` | `CreateNextVersion` | `NotificationTemplateTests.CreateNextVersion_is_allowed_from_an_archived_version` |
| Archivage depuis `Draft`/`Published` | `Archive` | `NotificationTemplateTests.Archive_retires_a_published_template_and_raises_TemplateArchived`, `Archive_can_discard_a_draft` |
| `Archived` terminal | toutes | `NotificationTemplateTests.Archived_is_terminal` |
| Clé de template au format contrôlé | `TemplateKey.Create` | `NotificationValueObjectsTests.TemplateKey_rejects_an_invalid_key`, `TemplateKey_length_is_bounded` |
| Dates d'état = instant de l'événement (une lecture d'horloge) | `Create`, `MarkSent`, `RegisterFailure`, `ScheduleRetry`, `Cancel`, `Redact`, `Publish`, `Archive` | `NotificationTests.Create_stamps_the_notification_and_its_event_with_the_same_instant`, `MarkSent_stamps_the_attempt_the_notification_and_the_event_with_the_same_instant`, `RegisterFailure_stamps_the_attempt_and_its_events_with_the_same_instant`, `ScheduleRetry_computes_the_next_attempt_from_the_instant_of_its_event`, `Cancel_and_Redact_stamp_the_state_and_the_event_with_the_same_instant`, `NotificationTemplateTests.Publish_and_Archive_stamp_the_state_and_the_event_with_the_same_instant` |

## 5. Ce qui relève de l'Application / Infrastructure

- **Consentement** : l'Application lit le consentement dans Customers (`Consent` du client sur le canal) juste avant `Create`
  **et juste avant chaque `MarkSending`** d'une notification marketing, et le traduit en
  `ConsentProof.Granted/Denied(customerId, channel)`. Le domaine ne peut pas vérifier que la preuve est authentique ni qu'un
  message « Transactional » l'est réellement : la classification par cas d'usage reste une responsabilité de l'Application.
  Quand `MarkSending` rejette (`consent_not_granted`), le worker appelle `Cancel`. Sur `ConsentWithdrawn`, l'Application peut
  aussi annuler tout de suite les notifications marketing en attente du client ; ce n'est plus la seule protection.
- **Effacement sur `CustomerAnonymized`** : l'Application charge toutes les notifications du client (par `CustomerId`),
  `Cancel` celles qui peuvent encore partir (`Pending`, `Failed` avec tentatives restantes ; une tentative en vol doit d'abord
  être clôturée par `MarkSent` / `RegisterFailure`), puis `Redact` toutes (idempotent, donc rejouable). Les copies hors
  domaine (journaux du fournisseur, files, messages rendus) relèvent de l'Infrastructure.
- **Langue** : l'Application passe la sous-étiquette primaire (`fr-FR` -> `fr`) et décide du repli (langue du client, sinon du tenant).
- **Niveau plateforme** : seuls les cas d'usage plateforme (inscription, vérification d'email, mot de passe oublié) appellent
  `CreateForPlatform` / `CreatePlatformTemplate` ; la gestion des templates plateforme est réservée aux administrateurs de la
  plateforme. Le filtre global par tenant doit laisser lire les templates `TenantId IS NULL` (résolution) sans jamais
  permettre à un tenant de les modifier, et le worker d'envoi traite aussi les notifications sans tenant.
- **Unicité** : `ITemplateUniquenessChecker` (tenant `null` = plateforme) + index unique
  `(TenantId, Key, Channel, Language, Version)` comme garde finale — avec `TenantId` nullable, l'index doit traiter `NULL`
  comme une valeur (index filtré dédié ou `NULLS NOT DISTINCT`).
- Une seule version publiée « courante » par `(tenant?, key, channel, language)` : sur `TemplatePublished` de la version N+1,
  l'Application archive la version N (règle inter-agrégats) ; le moteur de rendu applique la règle de résolution du §3
  (template du tenant publié, sinon template plateforme).
- Rendu du template (substitution des variables, vérification qu'aucune ne manque, limites propres au canal, ex. longueur SMS).
- Fournisseurs d'envoi, idempotence côté fournisseur, accusés de réception.
- Worker : sélection des notifications dues (`Pending` et `NextAttemptAtUtc <= now`), **verrouillage optimiste** (deux workers :
  le second `MarkSending` est rejeté par `attempt_in_progress` ou par le conflit de version), reprise des tentatives orphelines
  (`RegisterFailure("timeout")`), décision d'appeler `ScheduleRetry` (erreur transitoire) ou `Cancel` (erreur définitive), choix
  de la `NotificationRetryPolicy` **à la création** seulement, réaction à `NotificationDeliveryAbandoned` (alerte, suivi).

## 6. Catalogue des prestataires (cahier des charges, étapes 1 à 3)

Voir `docs/NOTIFICATIONS-CAHIER.md` et `docs/NOTIFICATIONS-CONFORMITE.md`.

| Classe | Rôle DDD | Responsabilité | Erreurs |
|---|---|---|---|
| `NotificationChannel` | Enum (+ `NotificationChannels`) | Email, Sms, WhatsApp, Push, InApp ; valeurs définitives ; lien avec les canaux de consentement du noyau | `notifications.channel.unknown` |
| `NotificationProvider` | AggregateRoot (plateforme) | Prestataire d'un canal : code, nom, type de configuration, capacités, proposé ou non | `notifications.provider.*` |
| `NotificationProviderCode` | Value Object | Code stable en majuscules ; seul `IN_APP` est connu du domaine | `notifications.provider.code_invalid` |
| `NotificationProviderConfigurationType` | Enum | `Internal` (InApp), `PlatformOnly`, `TenantOnly`, `PlatformOrTenant` | `notifications.provider.configuration_type_*` |
| `NotificationProviderCapability` | Value Object | Canal, fonctionnalités (`NotificationFeature`) disponibles sur ce canal, pays (vide = tous), longueur max | `notifications.provider.feature_*`, `.content_feature_missing`, `.country_invalid`, `.too_many_countries`, `.max_content_length_invalid` |
| `CountryCode` | Value Object | ISO 3166-1 alpha-2 (copie locale, comme Payments) | `notifications.provider.country_invalid` |

- **Créé non proposé** : `Create` ajoute le prestataire au catalogue avec `IsEnabled = false` ; la plateforme range ses
  identifiants (mode plateforme) puis appelle `Enable`. Activer ou désactiver deux fois est refusé.
- **InApp** : livré par la plateforme elle-même, configuration `Internal`, sans compte ni identifiants ; aucun prestataire
  externe n'est `Internal`.
- **Capacités** remplacées en bloc, dans le canal du prestataire (`channel_mismatch` sinon) ; un nom, des capacités ou un
  type de configuration identiques ne changent rien et ne lèvent aucun événement.

## 7. Configuration par restaurant (étape 4)

| Classe | Rôle DDD | Responsabilité | Erreurs |
|---|---|---|---|
| `RestaurantNotificationConfiguration` | AggregateRoot (tenant) | Un prestataire du catalogue utilisé par un restaurant : mode, statut, activation, référence du coffre, établissements | `notifications.configuration.*` |
| `NotificationChannelAssignment` | Entity enfant | Le canal proposé dans un établissement : actif, priorité (repli entre prestataires d'un même canal) | `notifications.configuration.priority_out_of_range` |
| `NotificationProviderMode` | Enum | `PlatformManaged` (compte de la plateforme ; InApp), `TenantManaged` (compte du restaurant) ; `IsAllowedBy(ConfigurationType)` | `notifications.configuration.mode_*` |
| `NotificationConfigurationStatus` | Enum | `AwaitingCredentials`, `Configured` | — |
| `NotificationCredentialReference` | Value Object | Où sont rangés les identifiants (tenant ou plateforme + prestataire), jamais leur valeur | `notifications.configuration.credential_reference_invalid` |
| `LocationReference` | Value Object | Établissement (tenant, id, pays) construit par l'Application | `common.id.empty` |

- Une configuration par (restaurant, prestataire) ; elle vaut pour tous les établissements tant qu'aucun n'est affecté.
- `CanServe(null)` : un envoi du restaurant lui-même (sans établissement) n'est pas limité par les affectations.
- Contrat du prestataire changé par la plateforme (`ChangeConfigurationType`) : une configuration dont le mode n'est plus
  permis ne peut plus être activée ; la policy d'envoi (étape 17) refusera aussi d'envoyer par elle.

## 8. Templates (étape 5)

- **Versions gardées** ; `EditDraft` devient `UpdateContent` ; `Enable` / `Disable` : un modèle du restaurant désactivé
  laisse la place à celui de la plateforme.
- **Canal** : `NotificationChannel` (InApp compris). Sujet : obligatoire pour Email, titre facultatif pour Push et InApp,
  interdit pour Sms et WhatsApp.
- **Types** : `NotificationTypes` (catalogue du produit) donne pour chaque clé la finalité, la priorité
  (`NotificationPriority`) et les variables autorisées (`NotificationTypeDefinition`).
- **Variables** : `TemplateVariables` n'accepte que `{{ nom.pointé }}` ; un email marketing doit contenir
  `{{unsubscribe.url}}`.
- **Résolution** : `NotificationTemplateResolution.Resolve` (voir `docs/NOTIFICATIONS-CONFORMITE.md`, étape 5).

## 9. Préférences (étape 6)

- `NotificationPreference` (tenant) : choix d'un client ou d'un utilisateur (`RecipientId`), par finalité et canal
  (`ChannelPreference`, source `PreferenceSource`). Défaut : tout accepté, sauf le marketing pour un utilisateur.
- `NotificationPermission.Decide(type, canal, destinataire, préférences?, consentement?)` -> `PermissionDecision`
  (`OptedOut`, `NotACustomer`, `ConsentRequired`, `ConsentNotGranted`, `ConsentMismatch`).
- Types ESSENTIELS (`NotificationTypeDefinition.IsEssential`) : vérification d'email, réinitialisation, OTP.
- **Envoi anonyme** (`Anonymous` sur `SendNotificationCommand`, `NotificationTrigger`, `RequestOtpCommand`, posé par l'hôte
  pour le site public) : SMS et WhatsApp seulement vers un numéro du pays de l'établissement (sinon des établissements du
  restaurant), `PhoneCountries` ; au plus `Notifications:AnonymousSmsPerTenantPerHour` (défaut 100) par restaurant et par
  heure glissante, comptés en base (`PhoneCountryNotAllowed`, `AnonymousPhoneLimitReached` ; OTP :
  `notifications.otp.phone_country_not_allowed`, `notifications.otp.phone_limit_reached` en 429).

## 10. Notification (étape 7)

- **Données** : tenant (ou plateforme), `LocationId` (d'une `LocationReference` du même tenant), type (`TemplateKey`, avec
  `Purpose` et `Priority` recopiées), `RecipientId` (qui), `NotificationRecipient` (où), `ProviderCode`, `Language`,
  `NotificationContent` (sujet et corps RENDUS, liés au canal), `TemplateId?`, `RetryPolicy`, `ScheduledAtUtc`.
- **Cycle** : `Pending` (ou `Scheduled`) -> `Processing` (`StartProcessing`) -> `Sent` -> (`PartiallyDelivered` ->)
  `Delivered` ; `Processing` -> `Failed` -> `Pending` (`ScheduleRetry`) ; `Pending` / `Scheduled` / `Failed` -> `Cancelled`.
  `Delivered` et `Cancelled` terminaux. Un rapport de remise reçu pendant `Processing` clôt la tentative.
- **Programmation** : `Create(..., scheduledAtUtc)` ou `Schedule(at)` tant qu'aucune tentative n'a eu lieu ; date dans le
  futur (`schedule_in_past`).
- **Gardes** : IN_APP si et seulement si InApp ; contenu du même canal que le destinataire ; InApp = l'utilisateur
  destinataire ; marketing = client + consentement accordé (revérifié à `StartProcessing`), jamais plateforme ni in-app.
- **Effacement** : `Redact` une fois remis, annulé ou abandonné : adresse, contenu et messages du prestataire. Une remise
  attendue reste enregistrable.
- **Événements** : `NotificationCreated`, `NotificationScheduled`, `NotificationSent`, `NotificationPartiallyDelivered`,
  `NotificationDelivered`, `NotificationFailed`, `NotificationDeliveryAbandoned`, `NotificationRetryScheduled`,
  `NotificationCancelled`, `NotificationRedacted` ; ids et codes uniquement.

## 11. NotificationDelivery (étape 8)

- `NotificationDelivery` (entité enfant) : `ProviderCode` de la tentative, `ProviderMessageId?` (id donné par le
  prestataire, celui que citent ses webhooks), `Status` (`InProgress`, `Sent`, `PartiallyDelivered`, `Delivered`,
  `Failed`), `AttemptNumber`, `StartedAtUtc`, `CompletedAtUtc?`, `DeliveredAtUtc?`, `Failure?` / `FailureCode?`,
  `NextRetryAtUtc?`. Toutes ses méthodes sont `internal` : seule `Notification` la fait évoluer.
- `NotificationFailure` : code stable en majuscules (`INVALID_PHONE_NUMBER`), nature `Temporary` ou `Permanent`, message
  brut facultatif (≤ 500, jamais dans un événement ni dans `ToString`, effacé par `Redact`).
- Un échec PERMANENT n'est jamais relancé (`permanent_failure`) : la notification devient aussitôt « dead letter »
  (`IsDeliveryAbandoned`, `NotificationDeliveryAbandoned` avec le code). Idem quand les tentatives sont épuisées.
- Un échec signalé APRÈS l'acceptation (rebond) fait passer `Sent -> Failed` ; jamais après une remise (`already_delivered`).
- Rapports de remise et échecs tardifs : l'id cité doit être celui de la tentative (`provider_message_mismatch`).
- Relance par un prestataire de secours : `StartProcessing(clock, consent, provider)`.
- Événements : `NotificationSent` porte le code du prestataire ; `NotificationFailed` et `NotificationDeliveryAbandoned`
  portent le code d'échec (jamais le message).

## 12. Persistance : NotificationsDbContext (étape 10)

- Schéma `notifications`, historique des migrations dans ce schéma ; fabrique de conception lisant `NOTIFICATIONS_DB`.
- Filtres par tenant (deuxième ligne de défense) : `RestaurantNotificationConfiguration` et `NotificationPreference`
  par le kernel (`ITenantOwned`) ; `Notification` : tenant courant, ou plateforme seule sans tenant ;
  `NotificationTemplate` : tenant courant + plateforme ; `NotificationProvider` : aucun (catalogue).
- Le tenant est lu à chaque requête depuis `ITenantContext` (contexte authentifié), jamais depuis une saisie.
- Dispatcher d'outbox et migration au démarrage : étape 11, avec la première migration.

## Intégrations de prestataires réelles

Chaque intégration est une `INotificationSenderFactory` de l'Infrastructure (`Senders/`), enregistrée dans
`AddNotificationsInfrastructure` ; l'endpoint `/admin/notification-providers/integrations` les liste, et le catalogue
n'accepte un prestataire que si son code en a une (sinon `422 notifications.provider.integration_missing`).

| Code | Canal | Champs du compte (coffre) | Accusés de remise |
|---|---|---|---|
| `SMTP` | Email | `host`, `port`, `security` (`starttls`, `ssl`, `none`), `username`, `password`, `from_email`, `from_name` | aucun : un message accepté par le serveur reste « envoyé » |
| `WHATSAPP_META` | WhatsApp | `access_token`, `phone_number_id`, `app_secret`, `verify_token`, `template_name`, `template_language` | webhooks signés `X-Hub-Signature-256` (`delivered`, `read`, `failed`) |
| `IN_APP` | InApp | — | immédiat |
| `FAKE` | tous sauf InApp | `api_key`, `webhook_secret` | développement et tests (`EnableFakeProvider`) |
| `MAILJET` | Email | `api_key`, `secret_key`, `from_email`, `from_name`, `webhook_password` | Event API, mot de passe en Basic |
| `BREVO` | Email | `api_key`, `from_email`, `from_name`, `webhook_password` | webhook transactionnel, mot de passe en Basic |
| `BREVO_SMS` | SMS | `api_key`, `sms_sender`, `webhook_password` | webhook SMS, mot de passe en Basic |
| `SES` | Email | `region`, `access_key_id`, `secret_access_key`, `from_email`, `from_name` | aucun (rebonds via Amazon SNS, pas encore relié) |
| `RESEND` | Email | `api_key`, `from_email`, `from_name`, `webhook_secret` | webhooks signés Svix (`email.delivered`, `email.bounced`), 5 min de tolérance |
| `TWILIO` | SMS | `account_sid`, `auth_token`, `from` (numéro ou `MG…`), `webhook_password` | status callback (formulaire), mot de passe en Basic |
| `TWILIO_WHATSAPP` | WhatsApp | mêmes champs | idem ; hors fenêtre de 24 h, Twilio exige un modèle approuvé |
| `INFOBIP` | SMS | `base_url` (`*.api.infobip.com` seulement), `api_key`, `sender`, `webhook_password` | delivery reports, mot de passe en Basic |
| `TERMII` | SMS | `base_url` (`*.termii.com` seulement), `api_key`, `sender_id`, `webhook_secret` | webhooks signés `X-Termii-Signature` (HMAC-SHA512) |
| `AFRICASTALKING` | SMS | `username` (`sandbox` = test), `api_key`, `sender_id`, `webhook_password` | delivery reports (formulaire), mot de passe en Basic |
| `FCM` | Push | `service_account` (JSON du compte de service Firebase) | aucun : FCM ne confirme pas la remise |
| `ONESIGNAL` | Push | `app_id`, `rest_api_key` | aucun relié ; l'adresse est l'identifiant d'abonnement OneSignal |

Un prestataire du catalogue n'a qu'un canal : Brevo et Twilio ont donc deux intégrations (et deux comptes au coffre).
Toutes passent par HTTP sans SDK (`HttpNotificationSender`) : réseau coupé ou délai dépassé = échec temporaire ; 429 =
`RATE_LIMITED`, 401/403 = `PROVIDER_AUTH_FAILED`, 402 ou solde épuisé = `PROVIDER_NO_CREDIT` (temporaires) ; seul le
code d'erreur du prestataire est repris (`BREVO_…`, `TWILIO_…`, `INFOBIP_…`, `FCM_…`), jamais son message. Les URL de
base saisies (Infobip, Termii) sont limitées aux domaines du prestataire, en HTTPS, sans identifiants : un compte ne
peut pas faire appeler un autre serveur. Firebase : jeton OAuth obtenu par un JWT RS256 signé avec la clé privée,
gardé en mémoire jusqu'à son expiration. Amazon SES : requêtes signées AWS Signature V4, région vérifiée.

Webhooks non signés par le prestataire (Brevo, Twilio, Infobip, Africa's Talking, Mailjet) : l'URL déclarée chez lui
porte le mot de passe du compte, `https://restauspace:<webhook_password>@<api>/v1/notifications/webhooks/<CODE>` (ou
`…/<CODE>/{configurationId}` pour le compte d'un restaurant), comparé à temps constant.

Chaque événement est rangé sous un id qualifié par le compte signataire (`p:` plateforme, `t:{tenant}:` restaurant) : un
restaurant n'occupe pas l'id d'un autre, et ses rapports ne s'appliquent qu'à SES notifications. Un id sans signataire
n'est jamais appliqué (`signer_unknown`).

- **SMTP** (MailKit) : corps envoyé en HTML s'il commence par une balise courante, sinon en texte. `Message-ID` =
  id de la tentative, renvoyé comme `ProviderMessageId`. Erreurs traduites par code SMTP (destinataire refusé =
  `INVALID_EMAIL` permanent ; authentification = `PROVIDER_AUTH_FAILED` temporaire ; réseau = temporaire).
- **WhatsApp Cloud API** (Meta, API Graph `v23.0`, réglable par `WhatsAppMetaOptions`) : un message à l'initiative du
  restaurant doit utiliser un modèle approuvé. Le compte désigne UN modèle générique dont le corps est `{{1}}` ; le
  message rendu y est placé sur une ligne (retours à la ligne remplacés par « · »), 1 024 caractères au plus
  (`CONTENT_TOO_LONG` sinon). Le numéro part sans « + ». Seul le code d'erreur de Meta est repris (`WA_<code>` ou un
  code commun), jamais son message.
- **Webhooks Meta** : `POST /notifications/webhooks/WHATSAPP_META` (compte plateforme) ou
  `/notifications/webhooks/WHATSAPP_META/{configurationId}` (compte d'un restaurant) ; la vérification d'abonnement
  `GET` (`hub.mode`, `hub.verify_token`, `hub.challenge`) répond le défi seulement si le jeton est celui du compte.
- Les secrets ne sortent jamais : ni dans les logs, ni dans les échecs (le message brut du prestataire n'est pas repris),
  ni dans les réponses ; `ToString()` du compte SMTP ne montre que le serveur et le port.

## Campagnes : clients d'un établissement

`Campaign.OnlyLocationCustomers` limite l'audience aux clients ayant passé au moins une commande non annulée dans
l'établissement de la campagne (`LocationId` obligatoire : `notifications.campaign.location_required`). La liste vient
d'Orders (`ListLocationCustomersQuery`), le Host la passe à Customers (`among`) : le consentement marketing, le tag et la
fréquence s'appliquent comme avant. `GET /campaigns/audience?customersOf={locationId}` donne le compte. Colonne
`only_location_customers` (migration `CampaignLocationAudience`).
