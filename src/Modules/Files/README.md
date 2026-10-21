# Module Files

But : gérer les métadonnées et la politique des fichiers sans stocker le binaire dans le domaine.

## 1. Frontière d'agrégat

- `MediaFile` est la seule racine : métadonnées + cycle de vie d'un objet stocké. Pas d'entité enfant.
- Son identité est le `FileId` du shared kernel (c'est l'id que les autres modules référencent).
- Le propriétaire est référencé par `FileOwner` (code de type + `Guid`), jamais par navigation : Files ne connaît ni Catalog ni Reporting.
- Le binaire et le fournisseur de stockage sont hors domaine ; `StorageKey` est une clé opaque.

## 2. Classes

| Classe | Rôle DDD | Responsabilité | Transitions | Events | Erreurs |
|---|---|---|---|---|---|
| `MediaFile` | AggregateRoot (tenant-owned) | Métadonnées, contrôle de la politique à la déclaration **puis sur les faits observés** (`MarkAvailable`), cycle de vie | `Pending -> Available`, `Pending -> Quarantined`, `Available -> Quarantined`, `Pending/Available/Quarantined -> Deleted` (terminal) | `FileUploaded` (à `MarkAvailable`), `FileQuarantined`, `FileDeleted` | `files.media_file.*` |
| `FileUploadPolicy` | Value Object | Taille max (octets) + types MIME autorisés ; `FileUploadPolicy.Default` | — | — | `files.upload_policy.max_size_invalid`, `files.upload_policy.content_types_empty` |
| `ContentType` | Value Object | MIME normalisé `type/subtype` en minuscules, paramètres retirés | — | — | `files.content_type.invalid` |
| `FileName` | Value Object | Nom d'affichage : non vide, ≤ 255, sans séparateur de chemin, caractère de contrôle, caractère invisible / de direction (catégorie Unicode `Format`), séparateur de ligne / paragraphe ; ni uniquement des points, ni nom de périphérique Windows | — | — | `files.file_name.empty`, `.too_long`, `.invalid` |
| `StorageKey` | Value Object | Clé opaque du stockage, non vide, sans espace, ≤ 1024 | — | — | `files.storage_key.invalid` |
| `FileOwner` | Value Object (public : décrit l'agrégat d'un autre module) | `OwnerType` (code `<module>.<agrégat>`, au moins deux segments, ex. `catalog.product`) + `OwnerId` non vide | — | — | `files.owner.type_invalid`, `files.owner.id_empty` |
| `MediaFileStatus` | Enum | `Pending`, `Available`, `Quarantined`, `Deleted` | — | — | — |

## 3. Choix faits là où la spec était ouverte

- **Politique explicite** : `CreatePending` reçoit un `FileUploadPolicy`. Défaut fourni : 10 Mo ; `image/jpeg`, `image/png`,
  `image/webp`, `application/pdf`. La limite est inclusive (taille == max acceptée). La politique est lue **une fois**, à la
  déclaration : sa taille max est **épinglée** sur le fichier (`MediaFile.MaxSizeInBytes`) ; la changer plus tard n'invalide
  pas les fichiers existants et ne peut pas être assouplie le temps d'un appel.
- **Déclaré ≠ observé** : `CreatePending` ne voit que ce que l'uploader *déclare*. `MarkAvailable(actualSizeInBytes,
  detectedContentType, clock)` reçoit les faits mesurés par le stockage / le scanner et refuse, en laissant le fichier
  `Pending` et sans événement : taille ≤ 0 (`size_not_positive`), taille réelle au-dessus de la limite épinglée
  (`size_exceeds_limit`), taille réelle différente de la taille déclarée (`actual_size_mismatch`), type détecté différent du
  type déclaré (`content_type_mismatch`). L'appelant met alors le fichier en quarantaine. Comme le type déclaré a été validé
  contre la politique à la création, « détecté == déclaré » prouve aussi que le type réel est autorisé.
- **Vocabulaire canonique de `OwnerType`** : même grammaire pointée en minuscules que les autres VO « code » du projet
  (`[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+`, ≤ 64 car., normalisé trim + minuscules), **au moins deux segments** :
  `<module>.<agrégat>`, où `<module>` est le nom du module propriétaire en minuscules et `<agrégat>` le nom de sa racine
  en snake_case. Codes de référence : `catalog.product`, `catalog.category`, `tenants.tenant`, `tenants.location`,
  `identity.user`, `reporting.export_job`. Un type nu (`product`) est rejeté (`files.owner.type_invalid`). Tout nouveau code
  est ajouté ici avant d'être utilisé ; les consommateurs d'événements filtrent sur ces chaînes exactes.
- **Events avec propriétaire** : `FileQuarantined` et `FileDeleted` portent `OwnerType` + `OwnerId` comme `FileUploaded`, pour
  que le module propriétaire retire sa référence sans recharger le fichier.
- **Ids vides / horloge** : `CreatePending` passe le tenant par `Guard.NotEmpty` (`common.id.empty`) ; `FileOwner` garde son
  `OwnerId` (`files.owner.id_empty`, code conservé). `MarkAvailable`, `Quarantine`, `Delete` lisent l'horloge une seule fois :
  la date d'état est l'`OccurredAtUtc` de l'événement.
- **Quarantined ne revient pas à Available** : un fichier mis en quarantaine est supprimé, puis ré-uploadé si c'était un faux
  positif. Plus simple et plus sûr qu'une « libération » dont la spec ne parle pas.
- `FileUploaded` est levé par `MarkAvailable` (le binaire est réellement là), pas par `CreatePending` (simple intention, aucun event).
- `Quarantine` exige une raison (≤ 500 caractères), conservée dans `QuarantineReason`.
- `Delete` est permis depuis tout état non supprimé ; `FileDeleted` porte la `StorageKey` pour que le handler supprime le binaire.
- `FileName` **rejette** un nom hostile (`../x`, `a/b`, `a\b`, caractères de contrôle, `.` / `..`) au lieu de le réécrire :
  le nettoyage cosmétique (ex. garder le dernier segment) est un choix d'UI fait par l'Application avant d'appeler le domaine.
  Sont aussi rejetés (`files.file_name.invalid`) : les caractères de catégorie Unicode `Format` (override droite-à-gauche
  U+202E qui affiche `invoice<RLO>gnp.exe` comme un `.png`, isolats et marques de direction, zéro-largeur U+200B/U+200D/U+FEFF,
  trait d'union conditionnel, caractères « tag » hors BMP), `LineSeparator` U+2028, `ParagraphSeparator` U+2029, un
  surrogate isolé, un nom fait uniquement de points, et les noms de périphériques Windows `CON`, `PRN`, `AUX`, `NUL`,
  `COM1`..`COM9`, `LPT1`..`LPT9`, sans tenir compte de la casse, avec ou sans extension (`nul.txt`, `Com1.tar.gz`).
  Conséquence assumée : un nom contenant un emoji composé par U+200D (ZWJ) est refusé ; les emoji simples passent.
- `ContentType` : les jokers (`image/*`) ne sont pas des types valides ; la liste autorisée est donc exacte.
- `OwnerType` + `OwnerId` de la spec sont regroupés dans `FileOwner` ; `Size` s'appelle `SizeInBytes`.
- `CanBeServed` : seul un fichier `Available` peut être servi.

## 4. Matrice invariant -> méthode -> test

| Invariant | Méthode | Test |
|---|---|---|
| Taille > 0 | `MediaFile.CreatePending` | `MediaFileTests.CreatePending_rejects_a_non_positive_size` |
| Taille sous la limite (limite incluse) | `MediaFile.CreatePending` | `MediaFileTests.CreatePending_rejects_a_size_over_the_limit`, `MediaFileTests.CreatePending_accepts_a_size_equal_to_the_limit` |
| MIME autorisé | `MediaFile.CreatePending` | `MediaFileTests.CreatePending_rejects_a_content_type_outside_the_policy`, `MediaFileTests.CreatePending_with_the_default_policy_rejects_an_executable` |
| La politique vient du paramètre, pas d'une constante | `MediaFile.CreatePending` | `MediaFileTests.CreatePending_applies_the_policy_it_is_given` |
| Tenant obligatoire et immuable | `MediaFile.CreatePending` | `MediaFileTests.CreatePending_rejects_a_default_tenant`, `MediaFileTests.Tenant_is_guarded_against_cross_tenant_references` |
| Création en `Pending`, sans event | `MediaFile.CreatePending` | `MediaFileTests.CreatePending_records_metadata_without_raising_an_event` |
| `Pending -> Available` + `FileUploaded` | `MarkAvailable` | `MediaFileTests.MarkAvailable_makes_a_pending_file_servable_and_raises_FileUploaded` |
| Taille réelle == taille déclarée | `MarkAvailable` | `MediaFileTests.MarkAvailable_rejects_an_actual_size_different_from_the_declared_one`, `MarkAvailable_rejects_a_non_positive_actual_size` |
| Taille réelle sous la limite épinglée à la création | `CreatePending`, `MarkAvailable` | `MediaFileTests.MarkAvailable_rejects_an_actual_size_over_the_limit_pinned_at_creation`, `The_size_limit_is_pinned_from_the_policy_given_at_creation` |
| Type détecté == type déclaré | `MarkAvailable` | `MediaFileTests.MarkAvailable_rejects_a_detected_content_type_different_from_the_declared_one` |
| Fichier refusé reste `Pending`, quarantaine possible | `MarkAvailable`, `Quarantine` | `MediaFileTests.A_file_whose_observed_facts_were_rejected_can_be_quarantined` |
| Date d'état = instant de l'événement | `MarkAvailable`, `Quarantine`, `Delete` | `MediaFileTests.State_timestamps_equal_the_event_instant` |
| `FileQuarantined` / `FileDeleted` portent le propriétaire | `Quarantine`, `Delete` | `MediaFileTests.Quarantine_isolates_a_pending_file`, `Delete_is_allowed_from_available_and_raises_FileDeleted_with_the_storage_key` |
| `MarkAvailable` seulement depuis `Pending` | `MarkAvailable` | `MediaFileTests.MarkAvailable_rejects_an_already_available_file` |
| `Quarantined` ne redevient pas `Available` | `MarkAvailable` | `MediaFileTests.MarkAvailable_rejects_a_quarantined_file` |
| `Pending -> Quarantined` | `Quarantine` | `MediaFileTests.Quarantine_isolates_a_pending_file` |
| `Available -> Quarantined`, plus servi | `Quarantine` | `MediaFileTests.Quarantine_isolates_an_available_file_which_can_no_longer_be_served` |
| Pas de double quarantaine | `Quarantine` | `MediaFileTests.Quarantine_rejects_an_already_quarantined_file` |
| Raison de quarantaine obligatoire et bornée | `Quarantine` | `MediaFileTests.Quarantine_requires_a_reason`, `MediaFileTests.Quarantine_rejects_a_reason_over_the_limit` |
| Suppression depuis tout état non supprimé + `FileDeleted` | `Delete` | `MediaFileTests.Delete_is_allowed_from_pending`, `Delete_is_allowed_from_available_and_raises_FileDeleted_with_the_storage_key`, `Delete_is_allowed_from_quarantined` |
| `Deleted` terminal | toutes | `MediaFileTests.Deleted_is_terminal` |
| Aucun setter public | `MediaFile` | `MediaFileTests.Aggregate_exposes_no_public_setter` |
| Nom non vide | `FileName.Create` | `FileValueObjectsTests.FileName_rejects_an_empty_name` |
| Nom sans séparateur de chemin | `FileName.Create` | `FileValueObjectsTests.FileName_rejects_path_separators_and_control_characters` |
| Nom sans caractère invisible ni de direction, sans séparateur de ligne / paragraphe | `FileName.Create` | `FileValueObjectsTests.FileName_rejects_invisible_and_direction_changing_characters`, `FileName_rejects_line_and_paragraph_separators`, `FileName_rejects_a_lone_surrogate` |
| Nom ≠ suite de points, ≠ périphérique Windows | `FileName.Create` | `FileValueObjectsTests.FileName_rejects_a_name_made_only_of_dots`, `FileName_rejects_windows_reserved_device_names` |
| Les noms inoffensifs passent (Unicode, emoji, `console.log`, `com10.txt`) | `FileName.Create` | `FileValueObjectsTests.FileName_accepts_harmless_names` |
| Nom borné | `FileName.Create` | `FileValueObjectsTests.FileName_length_is_bounded` |
| MIME normalisé `type/subtype` | `ContentType.Create` | `FileValueObjectsTests.ContentType_is_normalized`, `ContentType_rejects_an_invalid_mime_type`, `ContentType_length_is_bounded` |
| Clé de stockage opaque non vide | `StorageKey.Create` | `FileValueObjectsTests.StorageKey_rejects_an_empty_or_whitespace_key`, `StorageKey_length_is_bounded` |
| Propriétaire : type `<module>.<agrégat>` (≥ 2 segments) + id non vide | `FileOwner.Create` | `FileValueObjectsTests.FileOwner_rejects_an_invalid_type`, `FileOwner_accepts_the_canonical_owner_type_codes`, `FileOwner_type_length_is_bounded`, `FileOwner_type_accepts_the_maximum_length`, `FileOwner_rejects_an_empty_id` |
| Politique : taille max > 0 | `FileUploadPolicy.Create` | `FileValueObjectsTests.Policy_rejects_a_non_positive_max_size` |
| Politique : au moins un MIME | `FileUploadPolicy.Create` | `FileValueObjectsTests.Policy_requires_at_least_one_content_type` |
| Politique par défaut raisonnable | `FileUploadPolicy.Default` | `FileValueObjectsTests.Default_policy_allows_common_images_and_pdf_up_to_10_megabytes` |

## 5. Ce qui relève de l'Application / Infrastructure

- Fournisseur de stockage, URLs pré-signées, génération de la `StorageKey` (jamais dérivée du nom de fichier).
- **Vérité de la taille et du MIME** : l'Infrastructure mesure la taille réelle de l'objet stocké et renifle le type (magic
  bytes — jamais l'en-tête `Content-Type` de l'uploader), puis les passe à `MarkAvailable(actualSize, detectedType, clock)`.
  C'est le domaine qui tranche ; sur `actual_size_mismatch`, `size_exceeds_limit` ou `content_type_mismatch`, l'Application
  appelle `Quarantine` (raison = le code d'erreur). Le domaine ne peut pas prouver que les valeurs « observées » le sont
  vraiment : un appelant qui recopie les valeurs déclarées contourne le contrôle.
- **Propriétaire** : l'Application construit `FileOwner` avec un code du vocabulaire canonique (§3), jamais une saisie.
- Antivirus (déclenche `Quarantine`), suppression physique du binaire sur `FileDeleted`, nettoyage des `Pending` abandonnés.
- Choix de la politique par tenant/plan/type de propriétaire (elle est passée au domaine).
- Vérifier que le propriétaire existe et appartient au tenant ; verrouillage optimiste sur `MediaFile`.

## 6. Persistance (étape 1)

Schéma `files`, historique des migrations dans le même schéma. Les octets ne sont JAMAIS en base : seulement les
métadonnées.

| Table | Contenu | Gardes en base |
|---|---|---|
| `media_files` | Agrégat `MediaFile` ; propriétaire en une colonne `type/uuid` | Index unique `storage_key` ; index `(tenant_id, owner)` ; index `(status, created_at_utc)` (job) ; CHECK statut, taille (0 < taille <= limite épinglée), raison de quarantaine, date de suppression ; version `xmin` |
| `outbox_messages` | Événements à publier | — |

Migration à générer :

```bash
dotnet ef migrations add InitialCreate --project src/Modules/Files/Files.Infrastructure --output-dir Persistence/Migrations
```

## 7. Stockage Cloudflare R2, cas d'usage et API (étapes 2 à 6)

### Stockage

- **R2 par son API compatible S3**, adressage par chemin (`https://{AccountId}.r2.cloudflarestorage.com/{Bucket}/{clé}`),
  signature **SigV4** écrite dans le module (région `auto`, service `s3`) : aucune dépendance au SDK AWS. La signature
  est vérifiée sur l'exemple publié par AWS (`SigV4Tests`).
- **Clé de stockage** : `tenants/{tenant}/{type de propriétaire}/{aléatoire}`, jamais dérivée du nom du fichier.
- **Envoi direct** : l'API renvoie une URL PUT signée (15 min) dont le **type et la taille sont signés** : R2 refuse un
  envoi d'un autre type ou d'une autre taille. Les octets ne passent jamais par l'API.
- **Téléchargement** : URL GET signée (5 min), toujours en pièce jointe (`Content-Disposition: attachment`), seulement
  pour un fichier disponible.
- **Identifiants** : `Files:R2:AccountId`, `AccessKeyId`, `SecretAccessKey`, `Bucket` (facultatif : `Endpoint`). Fournis
  par variables d'environnement ou coffre de secrets ; le démarrage échoue s'ils manquent. La clé secrète ne sort
  jamais de `R2ObjectStorage` : ni journal, ni exception (une erreur ne porte que le statut HTTP), ni réponse.
  `Files:Storage=Memory` : stockage en mémoire, refusé hors développement.
- **`Files:Storage=Local` (développement)** : `LocalObjectStorage` écrit les octets sur le disque de l'API
  (`Files:Local:Directory`, volume `api-files` dans Docker) et sert `PUT`/`GET /v1/files/local/{jeton}`. Le jeton porte la
  méthode, la clé, le type et la taille exacts et une échéance, signés HMAC-SHA256 avec `Files:Local:SigningKey`
  (Base64, 32 octets au moins) : ces routes sont anonymes comme une URL pré-signée R2, la signature fait office
  d'autorisation. Le fichier sur disque est nommé par l'empreinte SHA-256 de la clé (aucun chemin dérivé de la clé ni du nom).
  `Files:Local:PublicBaseUrl` est l'adresse de l'API vue du navigateur. Refusé hors développement, comme `Memory`.
- **CORS** : à configurer sur le bucket R2 (origines du front, méthodes `PUT` et `GET`, en-tête `Content-Type`), sinon
  le navigateur ne peut pas envoyer directement.

### Vocabulaire des propriétaires et politiques (`FileOwnerTypes`)

| Code | Politique | Visé par le personnel |
|---|---|---|
| `catalog.product` | JPEG, PNG, WebP ; 5 Mo | Oui (produit vérifié dans Catalog) |
| `locations.location` | JPEG, PNG, WebP ; 5 Mo | Oui (établissement vérifié dans Locations) |
| `tenants.tenant` | JPEG, PNG, WebP ; 2 Mo | Oui (le logo du restaurant lui-même seulement) |
| `reporting.export_job` | CSV ; 50 Mo | Non : fichiers produits par le serveur |

`locations.location` remplace `tenants.location` du §3 (les établissements sont dans le module Locations).

### Cas d'usage

| Cas d'usage | Déclencheur | Règles appliquées ici |
|---|---|---|
| `RequestUpload` | Personnel (`files.upload`) | Restaurant accessible en écriture ; propriétaire connu et visé par le personnel, existant dans le tenant ; politique du type ; renvoie l'URL d'envoi |
| `CompleteUpload` | Personnel (`files.upload`) | Mesure de l'objet stocké (taille réelle, 512 premiers octets) ; antivirus ; type reconnu aux octets ; le domaine tranche ; écart = quarantaine avec le code ; rejouable |
| `DeleteFile` | Personnel (`files.manage`) | Suppression logique ; l'objet est effacé par la réaction à `FileDeleted` |
| `StoreGeneratedFile`, `ReadFileContent`, `DeleteOwnedFile` | Hôte (exports de Reporting) | Même vérification qu'un envoi ; aucune route |
| `DeleteAbandonedUpload` | Job `AbandonedUploadsWorker` | Fichier jamais confirmé après 24 h |

**Reconnaissance du type** (`ContentSniffer`) : JPEG, PNG, PDF, WebP par leur signature ; CSV reconnu comme texte UTF-8
sans octet binaire ; tout le reste `application/octet-stream`, donc refusé par le domaine. **Antivirus** : port
`IFileScanner` (aucun par défaut ; l'hôte peut en fournir un), appelé AVANT la mise à disposition.

### Intégration (hôte)

- `FileTenantsFromTenants`, `FileOwnersFromModules` (Catalog, Locations, Tenants).
- `ReportingExportFilesFromFiles` : les exports de Reporting sont stockés dans R2 par Files (propriétaire
  `reporting.export_job`), relus au téléchargement et supprimés à leur expiration. Le stockage en base de Reporting
  reste le défaut sans hôte (tests).

### Routes (`/files`, tenant du jeton, `Tenancy.TenantMemberPolicy`)

| Route | Permission |
|---|---|
| `GET /files?ownerType&ownerId`, `GET /files/{id}`, `GET /files/{id}/download` | `files.view` |
| `POST /files/uploads` (201 + URL d'envoi), `POST /files/{id}/complete` | `files.upload` |
| `DELETE /files/{id}` | `files.manage` |

Migration appliquée au démarrage (`MigrateFilesDatabaseAsync`) ; job coupable par `Files:RunWorkers=false`.

## 8. Tests (étape 7)

| Projet | Contenu |
|---|---|
| `Files.Domain.Tests` | Agrégat et objets-valeurs |
| `Files.Application.Tests` | `FilesUseCaseTests` : déclaration (URL, clé indépendante du nom), refus (propriétaire inconnu, type interne, type, taille, logo d'un autre), tenant suspendu, confirmation et rejeu, envoi absent, fichier déguisé ou d'une autre taille, antivirus, suppression, upload abandonné, fichier généré (CSV, binaire refusé), isolation, reconnaissance du type |
| `Files.Infrastructure.Tests` | `FilesModelTests` ; `SigV4Tests` (exemple AWS, encodage du chemin) ; `FilesPersistenceTests` : relecture, isolation, liste par propriétaire, clé unique, file du job, `FileDeleted` puis effacement de l'objet |
| `Files.Api.Tests` | Authentification, permissions, parcours complet (déclaration, envoi direct, confirmation, liste, lien), fichier déguisé, erreurs, isolation, suppression |

## 9. Audit (étape 8)

| Point | Application | Vérification |
|---|---|---|
| TenantId toujours issu du contexte | Aucune route ne reçoit de tenant ; clé préfixée par le tenant | `FilesEndpointsTests`, `FilesUseCaseTests` |
| Lecture croisée impossible | Filtre EF ; 404 sur l'id d'un autre ; propriétaire vérifié dans le tenant | `FilesPersistenceTests`, `FilesEndpointsTests` |
| Secrets R2 | Jamais journalisés, jamais dans une exception ni une réponse ; démarrage refusé s'ils manquent | Revue (`R2ObjectStorage`, `R2Options`) |
| Contenu non fiable | Type reconnu aux octets, taille mesurée, antivirus avant mise à disposition ; quarantaine sinon | `FilesUseCaseTests`, `FilesEndpointsTests` |
| Nom hostile | Rejeté par le domaine ; jamais utilisé dans la clé ; téléchargement en pièce jointe | `FileValueObjectsTests`, `FilesUseCaseTests` |
| Accès aux octets | URLs signées courtes, un objet par URL, type et taille signés à l'envoi | `SigV4Tests` |
| Nettoyage | Objet effacé sur `FileDeleted` (idempotent) ; uploads abandonnés supprimés | `FilesPersistenceTests`, `FilesUseCaseTests` |
| Journal | Ids seulement ; jamais une URL signée | Revue |

Limites connues : pas d'antivirus branché par défaut ; miniatures préparées par le navigateur (P2 : `thumbnail_of`), pas
de variantes générées par le serveur (aucune bibliothèque d'image) ; pas d'URL publique
(menu en ligne) : un client anonyme passera par une route dédiée ou un bucket public séparé ; un fichier mis en
quarantaine garde son objet jusqu'à sa suppression.
