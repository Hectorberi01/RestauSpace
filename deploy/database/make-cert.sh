#!/usr/bin/env bash
# Certificat TLS auto-signé pour PostgreSQL (10 ans). Le client se connecte en « require » : chiffré, sans vérifier
# l'autorité. Pour vérifier aussi l'identité du serveur, utiliser verify-full avec ce certificat côté client.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p tls
openssl req -new -x509 -days 3650 -nodes -subj "/CN=${1:-saasrestaurant-db}" \
  -keyout tls/server.key -out tls/server.crt
# L'utilisateur postgres de l'image alpine a l'uid 70 ; la clé doit lui appartenir et n'être lisible que par lui.
sudo chown 70:70 tls/server.key tls/server.crt
sudo chmod 600 tls/server.key
echo "Certificat créé dans tls/"
