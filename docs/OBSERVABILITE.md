# Observabilité — SaasRestaurant

Pile : OpenTelemetry (dans l'API) → OpenTelemetry Collector → Prometheus (métriques), Loki (logs), Tempo (traces)
→ Grafana (tableaux de bord) ; Prometheus et Loki évaluent les règles d'alerte, Alertmanager les achemine (email,
Telegram, Slack, Discord, webhook).

```
API .NET ──OTLP──► Collector ──► Prometheus ◄── node-exporter, postgres-exporter
                        │              │  règles ──► Alertmanager ──► email / Telegram / Slack / Discord / webhook
                        ├──► Loki ─────┘  règles ──┘
                        └──► Tempo ──(service graph, span metrics)──► Prometheus
                                     Grafana lit les quatre (Prometheus, Loki, Tempo, Alertmanager)
```

## Démarrer

```bash
cd deploy/observability
cp .env.example .env            # renseigner GRAFANA_ADMIN_PASSWORD et POSTGRES_EXPORTER_DSN
docker compose up -d
```

| Service | URL locale |
|---|---|
| Grafana | http://localhost:3000 (dossier « SaasRestaurant ») |
| Prometheus | http://localhost:9090 |
| Alertmanager | http://localhost:9093 |
| Loki / Tempo | http://localhost:3100 / http://localhost:3200 |
| OTLP (pour l'API) | grpc `localhost:4317`, http `localhost:4318` |

Puis lancer l'API : en développement, `Observability:OtlpEndpoint` vaut déjà `http://localhost:4317`. Sans cette
valeur (production par défaut, tests), rien n'est exporté.

Un port déjà pris sur la machine (« port is already allocated ») : changer `GRAFANA_PORT`, `PROMETHEUS_PORT`,
`ALERTMANAGER_PORT`, `LOKI_PORT`, `TEMPO_PORT`, `OTLP_GRPC_PORT` ou `OTLP_HTTP_PORT` dans `.env` ; seul le port côté
machine change, les services continuent de se parler entre eux sur leurs ports habituels. Si `OTLP_GRPC_PORT` change,
mettre la même valeur dans `Observability:OtlpEndpoint` de l'API.

Tous les ports sont liés à `127.0.0.1` (`BIND_ADDRESS`). En production, seul Grafana est exposé, derrière Traefik
(HTTPS) ; l'API rejoint le réseau Docker `saasrestaurant-observability` et utilise
`Observability__OtlpEndpoint=http://otel-collector:4317`.

## API conteneurisée

`Dockerfile` (racine) : build multi-étapes .NET 10, image d'exécution `aspnet:10.0` sous l'utilisateur non root
`app`, contrôle de santé sur `/health`. Le `docker-compose.yml` de la racine la lance sur le réseau
`saasrestaurant-observability`, avec `Observability__OtlpEndpoint=http://otel-collector:4317` et les migrations
appliquées au démarrage (développement uniquement).

```bash
cd deploy/observability && docker compose up -d      # d'abord : crée le réseau partagé
cd ../.. && docker compose --profile api up -d --build
```

| | |
|---|---|
| API | http://localhost:5080 (`API_PORT` pour changer) |
| OpenAPI | http://localhost:5080/openapi/v1.json |
| Santé | http://localhost:5080/health |
| Logs | `docker compose logs -f api`, ou Grafana > Explore > Loki `{service_name="saasrestaurant-api"}` |

Sans le profil `api`, `docker compose up -d` ne démarre que PostgreSQL (développement avec `dotnet run`).

## Ce que l'API émet

| Signal | Contenu |
|---|---|
| Traces | requêtes HTTP (`saasrestaurant.tenant_id` ajouté), appels HTTP sortants, PostgreSQL (Npgsql), et chaque traitement d'outbox rattaché à la trace de la requête qui a produit l'événement (`trace_parent` stocké avec le message) |
| Logs | tous les logs .NET, avec `trace_id`, `span_id`, `TenantId`, `UserId`, `CorrelationId` |
| Métriques techniques | HTTP serveur et client, runtime .NET, Kestrel, limitation de débit, `Idempotency-Key` par issue |
| Outbox | livraisons par module, consommateur et issue ; durée des consommateurs ; délai avant distribution ; file morte ; messages en attente et âge du plus ancien |
| Métier | commandes passées, terminées, annulées ; chiffre d'affaires et encaissements par devise ; paiements en échec ; remboursements ; restaurants et abonnements par statut ; MRR ; événements d'abonnement, de restaurant et de notification |

Les métriques métier par restaurant portent le label `tenant_id` (un identifiant, aucune donnée personnelle).
Au-delà de quelques milliers de restaurants, retirer ce label ou agréger avant Prometheus.

Ne quittent jamais l'API : secrets, jetons, en-têtes `Authorization`, `Cookie`, `X-Api-Key`, chaînes de requête
(masquées par l'instrumentation puis supprimées par le collecteur), messages d'exception (seul le code d'erreur est
mis sur les spans).

## Configuration de l'API

```json
"Observability": {
  "ServiceName": "saasrestaurant-api",
  "OtlpEndpoint": "http://otel-collector:4317",
  "TraceSampleRatio": 1.0,
  "PlatformMetrics": { "Enabled": true, "IntervalSeconds": 60 }
}
```

`TraceSampleRatio` : 0,1 conserve une trace sur dix (les traces enfants suivent la décision du parent).
`PlatformMetrics` : relevé périodique des restaurants, abonnements, MRR et files d'outbox.

## Tableaux de bord

| Tableau | Contenu |
|---|---|
| API et runtime | débit, erreurs 5xx et 4xx par route, latences p50/p95/p99, routes les plus lentes, idempotence, appels sortants, mémoire, GC, threads, exceptions, derniers logs d'erreur |
| Outbox et messages | files en attente, âge, file morte, livraisons par issue, consommateurs lents ou en échec, logs de l'outbox |
| Métriques métier | filtres restaurant et devise ; commandes, CA, panier moyen, annulations, top restaurants ; paiements, échecs, remboursements ; restaurants, abonnements, MRR |
| Infrastructure | cibles surveillées, ingestion du collecteur, alertes actives, hôte, PostgreSQL, graphe de services Tempo |

Navigation : d'un point de métrique à sa trace (exemplars), d'une trace à ses logs, d'un log à sa trace.

## Alertes

| Alerte | Gravité | Déclencheur |
|---|---|---|
| ApiTelemetryMissing | critique | plus aucune donnée de l'API depuis 10 min |
| ApiHighErrorRate | critique | plus de 5 % de 5xx pendant 5 min |
| ApiSlowRoute | avertissement | p95 d'une route au-delà de 1 s pendant 10 min |
| ApiRateLimited | avertissement | plus d'une réponse 429 par seconde pendant 10 min |
| IdempotencyKeyReuse | avertissement | plus de 10 clés réutilisées avec un autre contenu en 15 min |
| OutboxBacklog | critique | un message attend depuis plus de 5 min |
| OutboxDeadLetter | avertissement | un message entre en file morte |
| OutboxDeadLettersPiling | critique | plus de 20 messages en file morte pendant 30 min |
| OutboxConsumerFailing | avertissement | plus de 5 échecs d'un consommateur en 10 min |
| PlatformSnapshotStale | avertissement | relevé de plateforme absent ou vieux de plus de 10 min |
| PaymentFailureRateHigh | critique | plus de 20 % de paiements en échec sur 30 min (5 au moins) |
| RefundSpike | avertissement | plus de 30 % de l'encaissé remboursé en 1 h |
| CancellationSpike | avertissement | plus d'une commande sur quatre annulée en 1 h |
| OutboundCallsSlow | avertissement | appels sortants à plus de 3 s (p95) |
| ApiErrorLogBurst (Loki) | avertissement | plus de 20 logs d'erreur en 5 min |
| ApiCriticalLog (Loki) | critique | un log critique |
| TargetDown, PostgresDown, PostgresConnectionsHigh, PostgresDeadlocks, HostDiskAlmostFull, HostMemoryHigh, HostCpuHigh | selon le cas | infrastructure |

Acheminement : critique → email, Telegram, Slack, Discord, webhook (rappel toutes les heures) ; avertissement →
Telegram, Slack, Discord, webhook (toutes les 4 h) ; une alerte critique masque l'avertissement du même nom.

### Activer les canaux

1. Dans `.env` : `ALERTMANAGER_TEMPLATE=alertmanager.yml`, puis `SMTP_SMARTHOST`, `SMTP_FROM`, `SMTP_USERNAME`,
   `ALERT_EMAIL_TO`, `TELEGRAM_CHAT_ID`, `SLACK_CHANNEL`.
2. Dans `secrets/` (jamais versionné) : `smtp_password`, `telegram_bot_token`, `slack_webhook_url`,
   `discord_webhook_url`, `webhook_url`.
3. Un canal inutile : retirer son bloc de `alertmanager/alertmanager.yml`.
4. `docker compose up -d alertmanager`.

Sans cela, `alertmanager.minimal.yml` n'envoie qu'au webhook générique.

## Vérifications faites

Configurations validées avec les binaires officiels (`promtool`, `amtool`, `otelcol-contrib validate`,
`loki -verify-config`). Chaîne testée de bout en bout (Collector, Prometheus, Loki, Tempo, Alertmanager) avec des
données OTLP de même forme que celles de l'API : noms de métriques, règles d'enregistrement, alerte transmise à
Alertmanager, logs reliés aux traces, chaîne de requête bien supprimée. Les 95 requêtes des tableaux de bord sont
valides. Grafana et l'API .NET n'ont pas pu être lancés dans cet environnement.
