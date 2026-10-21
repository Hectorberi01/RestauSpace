# Audit : conteneurs en production

Date : 17 octobre 2026. Périmètre : ce que le dépôt définit aujourd'hui, et ce qu'il faut pour une mise en production
sur un serveur unique (Docker Compose derrière Traefik).

## 1. État actuel du dépôt

| Fichier | Contenu | Prêt pour la production ? |
|---|---|---|
| `Dockerfile` (racine) | API .NET 10, multi-étapes, utilisateur non root, contrôle de santé `/health` | Oui |
| `docker-compose.yml` (racine) | PostgreSQL + API | Non : prévu pour le développement (mot de passe par défaut, port 5433 ouvert sur toutes les interfaces, migrations au démarrage) |
| `deploy/observability/docker-compose.yml` | 9 services d'observabilité | Oui, ports liés à `127.0.0.1` |
| Console, site, super-admin | Aucun `Dockerfile` | Non |
| Traefik, sauvegardes PostgreSQL | Rien | Non |

**Constat** : aucun fichier ne décrit la production complète. Seules l'API et l'observabilité sont conteneurisées.

## 2. Inventaire des conteneurs nécessaires

### Applicatif : 6 conteneurs permanents

| # | Conteneur | Rôle | Pourquoi un conteneur à part |
|---|---|---|---|
| 1 | `traefik` | HTTPS (Let's Encrypt), routage par domaine | Point d'entrée unique ; l'API et Grafana n'écoutent que sur le réseau interne |
| 2 | `api` | API .NET + 17 traitements d'arrière-plan (outbox, notifications, abonnements, webhooks, rapports…) | Monolithe modulaire : un seul processus |
| 3 | `console` | `restaurantos-app` (Next.js) | Serveur Node requis : `proxy.ts` (CSP avec nonce par requête) |
| 4 | `site` | `restaurantos-site` (Next.js) | Serveur Node requis : menus publics régénérés toutes les 60 s (`revalidate`) |
| 5 | `super-admin` | `restaurantos-super-admin` (Next.js) | Serveur Node requis : `proxy.ts` ; à réserver à une adresse d'accès restreint |
| 6 | `postgres` | PostgreSQL 16, un schéma par module | Sauf base managée (voir 4.2) |

**Aucun conteneur pour** Redis, Kafka ni stockage objet :
- le cache est en mémoire ;
- les événements passent par l'outbox PostgreSQL ;
- les fichiers vont sur Cloudflare R2 ;
- les e-mails et SMS partent chez les prestataires externes.

### Observabilité : 8 conteneurs permanents + 1 d'initialisation

`otel-collector`, `prometheus`, `alertmanager`, `loki`, `tempo`, `grafana`, `node-exporter`, `postgres-exporter`,
plus `tempo-init`, qui règle les droits du volume puis s'arrête.

### Ponctuels (s'arrêtent après exécution)

| Conteneur | Quand |
|---|---|
| `migrations` | À chaque déploiement, AVANT de démarrer l'API (`Database:MigrateOnStartup` doit rester `false` en production) |
| `tempo-init` | Au démarrage de la pile d'observabilité |
| `backup` (recommandé) | `pg_dump` planifié vers R2 ; aucun n'existe aujourd'hui |

### Total

| Configuration | Permanents | Ponctuels |
|---|---|---|
| **Complète, auto-hébergée** | **14** (6 applicatifs + 8 observabilité) | 2 à 3 |
| Sans observabilité | 6 | 1 à 2 |
| Base PostgreSQL managée | 13 (ou 5 sans observabilité) | 1 à 2 |

## 3. Peut-on lancer plusieurs instances de l'API ?

Vérifié dans le code :

| Point | État |
|---|---|
| Outbox | **Sûr** : un seul distributeur par module (verrou consultatif PostgreSQL), les autres instances attendent |
| Webhooks de paiement | **Sûr** : `FOR UPDATE SKIP LOCKED` |
| Traitements périodiques (abonnements, réservations, notifications, webhooks sortants, fiscal…) | **Sûrs** : conflits gérés par la concurrence optimiste (le second passage est ignoré) |
| Purges (audit, idempotence, fichiers abandonnés) | **Sûres** : suppressions idempotentes |
| Métriques métier (MRR, restaurants) | **Sûres** : chaque instance les publie, les tableaux de bord prennent le `max` |
| Cache des restaurants et permissions | **À noter** : l'invalidation ne touche que l'instance qui traite l'outbox ; les autres gardent l'ancienne valeur jusqu'à 30 s (`Caching:TenantSeconds`, `Caching:PermissionSeconds`). Une permission retirée peut donc rester active jusqu'à 30 s |
| Limitation de débit (connexion, OTP, démo…) | **À noter** : compteurs en mémoire par instance ; avec N instances, la limite effective est multipliée par N |
| File d'e-mails Identity (vérification, mot de passe oublié) | **Problème, même avec une seule instance** : file en mémoire (`EmailQueue`, 1 000 messages) ; un redémarrage ou un déploiement perd les e-mails en attente |

**Recommandation** : une seule instance d'API au lancement. Passer à deux (déploiement sans coupure) après avoir :
1. rendu la file d'e-mails Identity durable (outbox ou module Notifications) ;
2. accepté ou corrigé le délai de 30 s sur le cache des permissions (invalidation par `LISTEN/NOTIFY` PostgreSQL, par exemple) ;
3. accepté que les limites de débit soient par instance, ou les déplacer dans PostgreSQL.

## 4. Pistes pour réduire le nombre de conteneurs

1. **Observabilité allégée** : garder `otel-collector`, `prometheus`, `alertmanager`, `grafana`, `node-exporter`,
   `postgres-exporter` (6) et retirer `tempo`/`tempo-init` (traces) et `loki` (logs). L'API ne trace plus rien (perte du
   lien logs ↔ traces). Gain : 2 permanents + 1 ponctuel.
2. **Base managée** (Neon, Supabase, Scaleway, OVH…) : retire `postgres` et la sauvegarde à gérer soi-même.
3. **Observabilité hébergée** (Grafana Cloud, palier gratuit) : seul `otel-collector` reste (ou aucun : l'API exporte
   directement en OTLP). Gain : 7 conteneurs.
4. **Super-admin hors production publique** : le lancer seulement sur un accès restreint (VPN, liste d'adresses
   Traefik). Il reste un conteneur, mais non exposé.

À ne PAS fusionner : les trois applications Next.js dans un même conteneur (cycles de déploiement, en-têtes de
sécurité et domaines différents), ni les traitements d'arrière-plan dans un conteneur séparé (le code ne le prévoit
pas ; ils démarrent avec l'API).

## 5. À faire avant la production

| Priorité | Action |
|---|---|
| Haute | `docker-compose.prod.yml` : Traefik, API, console, site, super-admin, PostgreSQL, réseau interne, secrets par variables d'environnement (jamais dans le fichier) |
| Haute | `Dockerfile` pour les trois applications Next.js (`output: 'standalone'`, utilisateur non root) ; `NEXT_PUBLIC_*` fixées au build |
| Haute | Conteneur `migrations` lancé avant l'API |
| Haute | Sauvegarde PostgreSQL planifiée et testée (restauration) |
| Haute | File d'e-mails Identity durable (perte au redémarrage) |
| Moyenne | Limites mémoire et CPU par conteneur, mesurées avec `docker stats` en recette |
| Moyenne | Grafana exposé seulement derrière Traefik (HTTPS + accès restreint), comme prévu dans `OBSERVABILITE.md` |
| Basse | Invalidation du cache entre instances, limites de débit partagées (seulement si plusieurs instances) |
