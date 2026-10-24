# Module Backups

But : demander, suivre, télécharger et restaurer les sauvegardes de la base, depuis le back-office de la plateforme.
L'API n'exécute rien : `pg_dump`, `age`, `rclone` et `pg_restore` tournent sur le serveur de base, dans l'agent
`deploy/database/backup-agent.sh`, qui lit la file en local.

## 1. Agrégats

| Racine | Rôle | Transitions | Events (audités) |
|---|---|---|---|
| `Backup` | Une sauvegarde : base, déclencheur, clé de stockage, taille, sha256, échéance | `Pending -> Running -> Uploading -> Completed` ; `Failed` ; `Completed -> Deleting -> Deleted` | `BackupRequested`, `BackupDownloadLinkCreated`, `BackupDeletionRequested` |
| `Restore` | Une restauration d'une sauvegarde `Completed` | `Pending -> Running -> Completed \| Failed` ; `Pending -> Cancelled` | `RestoreRequested`, `RestoreCancelled` |

- L'API ne pose que la demande, `Failed(timeout)`, `Deleting` et `Cancelled`. Le reste (Running, heartbeat, clé, Completed,
  Deleted, `previous_database_name`) est écrit par l'agent. Concurrence optimiste sur `xmin` : un heartbeat de l'agent
  annule la décision de l'API sur cette ligne.
- Agrégats de la plateforme, sans tenant. La ligne `backups.agent` (id = 1) est lue seulement.

## 2. Contrat avec l'agent (schéma `backups`)

- Tables `backups`, `restores`, `agent` ; statuts et déclencheurs en texte (noms des enums). L'outbox du module
  (`outbox_messages`, `outbox_deliveries`) sert seulement à l'audit.
- Index uniques partiels `ux_backups_active` et `ux_restores_active` (`((true))`), CHECK `id = 1` de l'agent : posés en
  SQL par la migration. Une insertion concurrente qui les heurte donne la même erreur que la vérification préalable.
- Codes d'échec en listes fermées (`FailureCodes`) ; jamais de texte libre.
- Clé de stockage : `{base}/{yyyy}/{MM}/{id}.dump.age`, revérifiée (regex et id) avant toute pré-signature.

## 3. Cas d'usage et règles

- Sauvegarde manuelle : refusée si une sauvegarde est active (`backups.backup.already_in_progress`, 409) ou une
  restauration (`backups.restore.in_progress`, 409).
- Planification (`BackupScheduleWorker`, chaque minute) : une demande `Scheduled` par jour à `Backups:Schedule:DailyAtUtc`,
  s'il n'y en a pas depuis ce créneau ; attend si un travail est actif.
- Maintenance (`BackupMaintenanceWorker`, chaque minute) : Running/Uploading (et restauration Running) sans heartbeat
  depuis `StaleAfterMinutes` -> `Failed(timeout)` ; sauvegardes `Completed` expirées -> `Deleting`, en gardant toujours
  les `KeepMin` plus récentes. Suppression manuelle : `backups.backup.keep_min_reached` (409). Les décisions de rétention
  sont sérialisées par un verrou PostgreSQL (plusieurs instances).
- Lien de téléchargement : `Completed` (`backups.backup.not_completed`, 409), stockage configuré
  (`backups.storage.not_configured`, 409), code 2FA (`backups.two_factor.invalid`, 422). URL GET pré-signée SigV4 de
  `DownloadLinkMinutes`, en pièce jointe ; le fichier reste chiffré.
- Restauration : `Completed`, `confirmDatabaseName` identique (`backups.restore.confirmation_mismatch`, 422), rien d'actif,
  code 2FA. `not_before_utc` = maintenant + `RestoreDelayMinutes` ; annulable tant qu'elle est Pending
  (`backups.restore.not_cancellable`, 409).
- La 2FA est vérifiée en dernier (un refus ne consomme pas le code) par le port `IAdminTwoFactor`, servi par l'hôte via
  Identity (`TwoFactorVerifier`, mêmes limites d'essais : 429 au-delà).
- Autres codes : `backups.backup.not_found`, `backups.restore.not_found` (404), `backups.backup.database_name_invalid`,
  `backups.concurrent_update`, `backups.auth.invalid_access_token`.

## 4. Routes (`Policies.PlatformAdmin`)

`GET /v1/admin/backups`, `GET /v1/admin/backups/{id}`, `POST /v1/admin/backups` (202), `DELETE /v1/admin/backups/{id}`
(202), `POST /v1/admin/backups/{id}/download-link`, `POST /v1/admin/backups/{id}/restore` (202),
`GET /v1/admin/backups/restores`, `POST /v1/admin/backups/restores/{id}/cancel`, `GET /v1/admin/backups/agent`,
`GET /v1/admin/backups/settings`.

## 5. Sécurité

- Clé S3 de l'API en LECTURE seule (pré-signature locale, aucun appel réseau). La clé d'écriture est sur le serveur de base.
- Ni URL signée, ni clé de stockage, ni secret dans les réponses (hors l'URL du lien), les events, l'audit ou les logs ;
  les workers ne journalisent que des ids et des compteurs.

## 6. Configuration (section `Backups`)

`DatabaseName` (vide : base de la chaîne de connexion), `RetentionDays` (30), `KeepMin` (7), `Schedule:Enabled` (true),
`Schedule:DailyAtUtc` (02:30), `RestoreDelayMinutes` (5), `StaleAfterMinutes` (60), `DownloadLinkMinutes` (5, 60 au plus),
`Storage:Endpoint|Region|Bucket|AccessKeyId|SecretAccessKey` (OVH : `https://s3.gra.io.cloud.ovh.net`, `gra`),
`RunWorkers` (true). Migration : `--migrate-only`, comme les autres modules.
