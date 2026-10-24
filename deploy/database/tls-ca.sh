#!/usr/bin/env bash
# TLS vérifié pour PostgreSQL installé sur le serveur (paquet Debian/Ubuntu, sans Docker).
# Crée une autorité (une fois) et un certificat serveur pour l'IP privée, l'active, puis affiche le certificat de
# l'autorité à recopier sur le serveur applicatif (deploy/production/tls/db-ca.crt).
#   sudo ./tls-ca.sh                  # IP 10.10.0.2, PostgreSQL 18
#   sudo ./tls-ca.sh 10.10.0.2 18     # IP, version
#   sudo ./tls-ca.sh --renew          # nouveau certificat serveur, même autorité (rien à changer côté applicatif)
set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "À lancer avec sudo" >&2; exit 1; }

renew=false
[ "${1:-}" = "--renew" ] && { renew=true; shift; }
ip="${1:-10.10.0.2}"
version="${2:-18}"
conf="/etc/postgresql/$version/main"
[ -d "$conf" ] || { echo "$conf introuvable (version de PostgreSQL ?)" >&2; exit 1; }

# Clé de l'autorité : lisible par root seulement. La sauvegarder hors du serveur puis la retirer d'ici si possible.
ca_dir=/root/restauspace-db-ca
tls_dir="$conf/tls"
umask 077
mkdir -p "$ca_dir" "$tls_dir"

if [ ! -f "$ca_dir/ca.crt" ]; then
  openssl req -new -x509 -days 3650 -nodes -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 \
    -subj "/CN=RestauSpace DB CA" -keyout "$ca_dir/ca.key" -out "$ca_dir/ca.crt"
  echo "Autorité créée : $ca_dir"
elif [ ! -f "$ca_dir/ca.key" ]; then
  echo "$ca_dir/ca.key absent : remettre la clé de l'autorité pour signer un certificat" >&2; exit 1
fi

if [ -f "$tls_dir/server.crt" ] && [ "$renew" = false ]; then
  echo "Certificat serveur déjà présent (--renew pour le refaire)"
else
  ext="$(mktemp)"; trap 'rm -f "$ext" "$tls_dir/server.csr"' EXIT
  printf 'subjectAltName=IP:%s\nbasicConstraints=CA:FALSE\nkeyUsage=digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n' "$ip" > "$ext"
  openssl req -new -nodes -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -subj "/CN=$ip" \
    -keyout "$tls_dir/server.key" -out "$tls_dir/server.csr"
  # 3 ans ; --renew avant l'échéance (date affichée en fin de script).
  openssl x509 -req -days 1095 -in "$tls_dir/server.csr" -CA "$ca_dir/ca.crt" -CAkey "$ca_dir/ca.key" \
    -CAcreateserial -extfile "$ext" -out "$tls_dir/server.crt"
  # Chaîne complète : le client vérifie avec le seul certificat de l'autorité.
  cat "$ca_dir/ca.crt" >> "$tls_dir/server.crt"
fi
chown -R postgres:postgres "$tls_dir"
chmod 700 "$tls_dir"; chmod 600 "$tls_dir/server.key"; chmod 644 "$tls_dir/server.crt"

# Réglages dans conf.d (inclus par défaut sous Debian) : postgresql.conf n'est pas modifié.
mkdir -p "$conf/conf.d"
cat > "$conf/conf.d/restauspace-tls.conf" <<EOF
# Généré par deploy/database/tls-ca.sh
ssl = on
ssl_cert_file = '$tls_dir/server.crt'
ssl_key_file = '$tls_dir/server.key'
ssl_min_protocol_version = 'TLSv1.2'
EOF
chown postgres:postgres "$conf/conf.d/restauspace-tls.conf"
grep -qE "^\s*include_dir\s*=\s*'conf.d'" "$conf/postgresql.conf" \
  || echo "Attention : include_dir = 'conf.d' absent de postgresql.conf, l'ajouter" >&2

# Les réglages ssl se rechargent sans redémarrage.
systemctl reload "postgresql@$version-main" 2>/dev/null || systemctl reload postgresql
sudo -u postgres psql -Atc "select 'ssl=' || current_setting('ssl') || ' cert=' || current_setting('ssl_cert_file')"

echo "Échéance du certificat serveur : $(openssl x509 -enddate -noout -in "$tls_dir/server.crt" | cut -d= -f2)"
echo
echo "Sur le serveur applicatif, dans deploy/production/tls/db-ca.crt (fichier public) :"
cat "$ca_dir/ca.crt"
