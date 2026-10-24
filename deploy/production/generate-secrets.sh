#!/usr/bin/env bash
# Produit les clés à copier dans api.env et .env (une seule fois : les changer ensuite rend illisibles les données
# chiffrées). Rien n'est écrit sur le disque.
set -euo pipefail
key() { openssl rand -base64 32; }
for name in Identity__AccessTokens__SigningKey Identity__TwoFactor__SecretKey Payments__CredentialsKey \
            Notifications__CredentialsKey Notifications__OtpKey Integrations__SecretsKey Fiscal__TokenKey \
            Storefront__TrackingKey Files__PublicMedia__SigningKey; do
  echo "$name=$(key)"
done
echo
echo "# .env"
echo "POSTGRES_PASSWORD=$(openssl rand -hex 24)"
echo "DATABASE_APP_PASSWORD=$(openssl rand -hex 24)"
echo "OBSERVABILITY_TOKEN=$(openssl rand -hex 32)"
