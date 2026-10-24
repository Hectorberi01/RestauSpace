# Configurer les sauvegardes RestauSpace

Trois endroits sont à configurer :

| Où | Quoi |
|---|---|
| OVH | Un bucket et deux utilisateurs S3 |
| Serveur applicatif (`vps-8233b491`) | La clé S3 en lecture seule dans `api.env`, puis le déploiement |
| Serveur de base (`vps-31d9dc8a`) | `age`, `rclone`, l'agent et son timer systemd |

Fais-les dans cet ordre : l'agent a besoin du schéma `backups`, que crée le déploiement de l'API.

---

## 1. OVH Object Storage

Dans l'espace client OVH : **Public Cloud › Stockage › Object Storage**.

1. **Créer le conteneur** :
   - API S3, région **GRA** (la même que les serveurs, si possible) ;
   - nom `restauspace-backups` ;
   - **privé** ;
   - versionnement non obligatoire ;
   - verrouillage d'objets (Object Lock) facultatif. Il protège contre une suppression par une clé volée, mais l'agent ne pourra plus supprimer les sauvegardes expirées avant la fin du verrouillage.
2. **Créer deux utilisateurs S3** (onglet *Utilisateurs S3*), puis, pour chacun, **générer les identifiants** (Access key et Secret key). Note-les tout de suite : la clé secrète ne s'affiche qu'une fois.
   - `restauspace-backup-agent` : droit **Lecture et écriture** sur `restauspace-backups`, ce qui comprend la suppression.
   - `restauspace-backup-api` : droit **Lecture seule** sur `restauspace-backups`.
3. Note l'**endpoint** affiché pour le conteneur, par exemple `https://s3.gra.io.cloud.ovh.net`, et la région en minuscules (`gra`).

---

## 2. Serveur applicatif : la clé en lecture seule de l'API

```bash
cd /opt/restauspace/deploy/production
nano api.env
```

Les valeurs par défaut conviennent pour le reste. Remplis au minimum :

```ini
Backups__Storage__Endpoint=https://s3.gra.io.cloud.ovh.net
Backups__Storage__Region=gra
Backups__Storage__Bucket=restauspace-backups
Backups__Storage__AccessKeyId=<access key de restauspace-backup-api>
Backups__Storage__SecretAccessKey=<secret key de restauspace-backup-api>
# Facultatif (valeurs par défaut) :
# Backups__RetentionDays=30          durée de conservation
# Backups__KeepMin=7                 jamais moins de 7 sauvegardes terminées
# Backups__Schedule__DailyAtUtc=02:30  sauvegarde quotidienne (UTC)
```

Déploie ensuite. La migration crée le schéma `backups`, et `db-grants` donne les droits au compte de l'API :

```bash
./deploy.sh
```

Vérifie dans le back-office, sur la page **Paramètres › Sauvegardes**. Le bandeau affiche « Stockage configuré » et « Agent hors ligne » ; c'est normal tant que l'agent n'est pas installé.

---

## 3. Serveur de base : l'agent

### 3.1 Copier les fichiers

Si le dépôt n'est pas cloné sur le serveur de base, copie les fichiers depuis le serveur applicatif (ou le Mac) :

```bash
scp deploy/database/backup-agent.sh deploy/database/backup-agent.env.example <utilisateur>@<serveur de base>:~/
```

Les deux fichiers doivent être **dans le même dossier** : `--install` copie l'exemple de configuration qui se trouve à côté du script.

### 3.2 Paquets et clé de chiffrement

```bash
sudo apt update && sudo apt install -y age rclone
sudo install -d -m 700 /root/restauspace-backup
sudo age-keygen -o /root/restauspace-backup/age-identity.txt
sudo chmod 600 /root/restauspace-backup/age-identity.txt
sudo grep 'public key' /root/restauspace-backup/age-identity.txt    # age1... : à mettre dans la configuration
```

**Copie tout de suite `age-identity.txt` hors du serveur**, dans un coffre (gestionnaire de mots de passe, clé USB chiffrée) :

```bash
sudo cat /root/restauspace-backup/age-identity.txt
```

Sans ce fichier, aucune sauvegarde n'est lisible, ni pour restaurer, ni pour ouvrir un fichier téléchargé.

### 3.3 Installer l'agent

```bash
chmod +x backup-agent.sh
sudo ./backup-agent.sh --install
```

Cette commande installe :

- le script, sous `/usr/local/sbin/restauspace-backup-agent` ;
- les unités `restauspace-backup.service` et `restauspace-backup.timer` ;
- la configuration `/etc/restauspace-backup.env` (droits 600), créée seulement si elle n'existe pas encore.

### 3.4 Remplir la configuration

```bash
sudo nano /etc/restauspace-backup.env
```

```ini
DB_NAME=es_restaurant
DB_OWNER=es_restaurant_user
DB_APP=es_restaurant_app
PG_BIN=/usr/lib/postgresql/18/bin

BACKUP_AGE_RECIPIENT=age1...                      # la « public key » du 3.2
BACKUP_AGE_IDENTITY=/root/restauspace-backup/age-identity.txt

RCLONE_REMOTE=ovh:restauspace-backups
RCLONE_CONFIG_OVH_TYPE=s3
RCLONE_CONFIG_OVH_PROVIDER=Other                  # rclone d'Ubuntu (< 1.64) ; OVHcloud si rclone récent
RCLONE_CONFIG_OVH_ENDPOINT=https://s3.gra.io.cloud.ovh.net
RCLONE_CONFIG_OVH_REGION=gra
RCLONE_CONFIG_OVH_ACCESS_KEY_ID=<access key de restauspace-backup-agent>
RCLONE_CONFIG_OVH_SECRET_ACCESS_KEY=<secret key de restauspace-backup-agent>
RCLONE_CONFIG_OVH_NO_CHECK_BUCKET=true

RESTORE_ENABLED=false                             # true seulement pour autoriser la restauration (étape 5)
#BACKUP_PING_URL=https://hc-ping.com/<uuid>       # alerte si une sauvegarde échoue (facultatif)
```

Teste l'accès au bucket avec la configuration de l'agent :

```bash
sudo bash -c 'set -a; . /etc/restauspace-backup.env; rclone lsd ovh: && rclone ls ovh:restauspace-backups'
```

Aucune erreur, et une liste vide : c'est bon. Une erreur `403` ou `SignatureDoesNotMatch` vient de la clé, de la région ou de l'endpoint.

### 3.5 Démarrer

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now restauspace-backup.timer
sudo systemctl start restauspace-backup.service       # un passage tout de suite
sudo journalctl -t restauspace-backup -n 50           # aucune erreur attendue
sudo restauspace-backup-agent --status                # agent et dernières sauvegardes
```

Si le service échoue sur l'accès au socket PostgreSQL ou à `/root/restauspace-backup` (durcissement systemd, jamais essayé sur un vrai serveur) :

```bash
sudo systemctl edit restauspace-backup.service
#   [Service]
#   ProtectHome=false
sudo systemctl start restauspace-backup.service && sudo journalctl -t restauspace-backup -n 20
```

---

## 4. Première sauvegarde

1. Dans le back-office, sur **Sauvegardes**, le bandeau doit indiquer **Agent en ligne**.
2. Clique sur **Sauvegarder maintenant**. Le statut passe par En attente, En cours, Envoi, puis Terminée, en une à quelques minutes selon la taille de la base.
3. Vérifie que le fichier est bien dans le bucket :
   ```bash
   sudo bash -c 'set -a; . /etc/restauspace-backup.env; rclone ls ovh:restauspace-backups'
   #   es_restaurant/2026/10/<id>.dump.age
   ```
4. Clique sur **Télécharger** et saisis le code 2FA. Pour vérifier le fichier sur le Mac :
   ```bash
   brew install age
   age -d -i age-identity.txt es_restaurant_<id>.dump.age > test.dump
   pg_restore --list test.dump | head        # liste des tables : le fichier est bon
   rm test.dump                              # il contient toutes les données en clair
   ```

Ensuite, l'API demande une sauvegarde chaque jour à 02:30 UTC, que l'agent exécute. Les sauvegardes expirées (30 jours) sont supprimées, en en gardant toujours au moins 7.

En cas d'échec, le back-office affiche la cause : `dump_failed`, `upload_failed`, etc. Le détail est dans :

```bash
sudo journalctl -t restauspace-backup --since today
```

---

## 5. Restauration (à tester avant l'ouverture)

La restauration est refusée par défaut (cause affichée : « désactivée »).

### Avant le test

- **Place disque** : il faut la taille de la base, plus 20 %, plus 1 Go, libres sur le serveur de base. Vérifie avec `df -h /var/lib/postgresql`.
- Active la restauration le temps du test :
  ```bash
  sudo sed -i 's/^RESTORE_ENABLED=.*/RESTORE_ENABLED=true/' /etc/restauspace-backup.env
  ```

### Le test

1. Dans le back-office, sur la ligne de la sauvegarde, clique sur **Restaurer**, tape `es_restaurant` et saisis le code 2FA.
2. Tu as 5 minutes pour annuler. Ensuite, l'agent :
   - restaure dans une base temporaire ;
   - coupe les connexions ;
   - échange les noms : l'ancienne base est gardée sous `es_restaurant_before_<date>`.
3. Sur le serveur applicatif, relance le déploiement pour rejouer les migrations et les droits :
   ```bash
   cd /opt/restauspace/deploy/production && ./deploy.sh "$(git -C ../.. rev-parse --short=12 HEAD)"
   ```
4. Vérifie la console et le site.

### Après le test

- Désactive à nouveau la restauration, si tu ne veux pas la laisser ouverte :
  ```bash
  sudo sed -i 's/^RESTORE_ENABLED=.*/RESTORE_ENABLED=false/' /etc/restauspace-backup.env
  ```
- Supprime l'ancienne base quand tout va bien (`\l` liste les bases) :
  ```bash
  sudo -u postgres psql -c '\l'
  sudo -u postgres psql -c 'DROP DATABASE es_restaurant_before_<date>;'
  ```

**Retour arrière**, si la restauration ne convient pas. Ce n'est pas un `deploy.sh` mais une opération sur le serveur de base : arrête l'API, puis renomme les bases (commandes dans `deploy/production/README.md`, section 5).

Refais ce test une fois par trimestre.

---

## Liste de contrôle

- [ ] Bucket `restauspace-backups` privé, en GRA
- [ ] Utilisateur S3 de l'agent (lecture et écriture) et de l'API (lecture seule)
- [ ] `Backups__Storage__*` dans `api.env`, puis `./deploy.sh`
- [ ] `age` et `rclone` installés, clé `age-identity.txt` créée et **copiée hors ligne**
- [ ] `/etc/restauspace-backup.env` rempli, `rclone ls` OK
- [ ] Timer actif, agent en ligne dans le back-office
- [ ] Première sauvegarde terminée, fichier déchiffré et lu sur le Mac
- [ ] Restauration testée, ancienne base supprimée, `RESTORE_ENABLED` remis à `false`
- [ ] (Facultatif) `BACKUP_PING_URL` pour être alerté d'un échec
