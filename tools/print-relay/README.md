# Relais d'impression ESC/POS

Relie une imprimante de tickets ESC/POS du réseau local (Epson TM-T20/T88, et compatibles en impression brute sur le port 9100) à RestauSpace. Les imprimantes Star CloudPRNT n'en ont pas besoin : elles interrogent l'API elles-mêmes.

## Installation

1. Dans la console : Paramètres, Imprimantes, « Nouvelle imprimante », type « ESC/POS par relais ». Copiez l'adresse affichée (elle n'est montrée qu'une fois et contient le jeton de l'imprimante).
2. Sur un ordinateur du restaurant toujours allumé (ou un Raspberry Pi), installez Node.js 20 ou plus récent et copiez `relay.mjs`.
3. Lancez :

```bash
RELAY_URL="https://api.exemple.com/v1/printing/relay/prt_…" PRINTER_HOST=192.168.1.50 node relay.mjs
```

| Variable | Rôle | Défaut |
| --- | --- | --- |
| `RELAY_URL` | Adresse copiée dans la console (https obligatoire) | |
| `PRINTER_HOST` | IP de l'imprimante sur le réseau local | |
| `PRINTER_PORT` | Port d'impression brute | `9100` |
| `POLL_SECONDS` | Intervalle d'interrogation | `3` |

4. Dans la console, « Test » imprime une page de test : l'imprimante passe « En ligne ».

## Démarrage automatique (Linux, systemd)

```ini
# /etc/systemd/system/restauspace-relay.service
[Unit]
Description=Relais d'impression RestauSpace
After=network-online.target
Wants=network-online.target

[Service]
EnvironmentFile=/etc/restauspace-relay.env
ExecStart=/usr/bin/node /opt/restauspace/relay.mjs
Restart=always
RestartSec=5
User=nobody

[Install]
WantedBy=multi-user.target
```

`/etc/restauspace-relay.env` contient `RELAY_URL=…` et `PRINTER_HOST=…`, lisible par root seulement (`chmod 600`).

## Fonctionnement

- Le relais demande le ticket suivant ; l'API répond 204 quand il n'y en a pas.
- Il l'envoie à l'imprimante en TCP, puis accuse réception (imprimé, ou code d'échec : `printer_unreachable`, `printer_refused`, `printer_timeout`).
- Un ticket sans accusé est renvoyé par l'API après 2 minutes, 5 fois au plus, puis expire après 2 heures.
- Jeton refusé (imprimante coupée ou nouvelle adresse générée) : le relais s'arrête avec le code 2.
- Ni l'adresse, ni le jeton, ni le contenu des tickets ne sont écrits dans le journal.
