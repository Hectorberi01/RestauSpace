// CI/CD RestauSpace (Jenkins sur le Mac). Deux jobs sur ce même fichier :
//   restauspace-ci       Multibranch, toutes les branches : tests .NET + vérification des 3 fronts. AUCUN identifiant.
//   restauspace-release  Pipeline (pas Multibranch), branche main seulement : mêmes vérifications, puis images
//                        poussées sur GHCR, validation manuelle et déploiement sur le VPS par SSH.
// Les identifiants sont rangés dans le dossier du seul job de livraison : une branche ou une demande de fusion qui
// modifierait ce fichier ne peut pas les lire (README de deploy/production, « Jenkins »).
//
// Identifiants (dossier du job de livraison) :
//   ghcr                  Username/password : compte GitHub + jeton (write:packages)
//   restauspace-build-env Secret file : deploy/production/build.env (voir build.env.example)
//   restauspace-vps-ssh   SSH Username with private key : compte de déploiement du VPS (clé limitée à deploy-ssh.sh)
// Variables globales (Administrer Jenkins > Système) :
//   RESTAUSPACE_DEPLOY_HOST=deploy@<ip du VPS>
//   RESTAUSPACE_DEPLOY_KNOWN_HOSTS=<ligne de ssh-keyscan vérifiée> : clé du VPS épinglée (sinon acceptée au 1er accès)
pipeline {
  agent any

  options {
    disableConcurrentBuilds()
    buildDiscarder(logRotator(numToKeepStr: '20'))
  }

  environment {
    // Jenkins sur macOS ne voit pas le PATH du terminal.
    PATH = "/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:/Applications/Docker.app/Contents/Resources/bin:${env.PATH}"
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_NOLOGO = '1'
    NEXT_TELEMETRY_DISABLED = '1'
    IMAGE_REGISTRY = 'ghcr.io/hectorberi01/'
    // Node.js pour les fronts, installé une fois dans JENKINS_HOME (absent de l'image Jenkins).
    NODE_VERSION = '22.20.0'
  }

  stages {
    stage('Version') {
      steps {
        script {
          // 12 caractères, comme deploy.sh sur le VPS.
          env.RELEASE = sh(script: 'git rev-parse --short=12 HEAD', returnStdout: true).trim()
          currentBuild.displayName = "#${env.BUILD_NUMBER} ${env.RELEASE}"
        }
      }
    }

    stage('Backend') {
      steps {
        sh 'dotnet restore SaasRestaurant.sln'
        sh 'dotnet build SaasRestaurant.sln -c Release --no-restore'
        // Certains tests démarrent PostgreSQL avec Testcontainers : Docker Desktop doit tourner.
        sh 'dotnet test SaasRestaurant.sln -c Release --no-build --logger trx --results-directory TestResults'
      }
      post {
        always {
          archiveArtifacts artifacts: 'TestResults/**/*.trx', allowEmptyArchive: true
        }
      }
    }

    stage('Node.js') {
      steps {
        sh '''
          dir="$JENKINS_HOME/tools/node-v$NODE_VERSION"
          if [ ! -x "$dir/bin/node" ]; then
            case "$(uname -m)" in
              x86_64) arch=x64 ;;
              aarch64|arm64) arch=arm64 ;;
              *) echo "Architecture non gérée : $(uname -m)" >&2; exit 1 ;;
            esac
            mkdir -p "$dir"
            curl -fsSL "https://nodejs.org/dist/v$NODE_VERSION/node-v$NODE_VERSION-linux-$arch.tar.gz" | tar -xz -C "$dir" --strip-components=1
          fi
          export PATH="$dir/bin:$PATH"
          # pnpm installé avec npm (le corepack de certaines versions de Node refuse les nouvelles clés de signature de npm).
          version=$(node -p "require('./restaurantos-app/package.json').packageManager.split('@')[1]")
          if [ "$(pnpm --version 2>/dev/null)" != "$version" ]; then
            npm install -g --no-fund --no-audit "pnpm@$version"
          fi
          node --version && pnpm --version
        '''
      }
    }
    

    stage('Front') {
      // Typecheck partout. Build Next.js hors livraison : en livraison il est fait par les images.
      parallel {
        stage('Console') {
          steps { dir('restaurantos-app') { script { front() } } }
        }
        stage('Site') {
          steps { dir('restaurantos-site') { script { front() } } }
        }
        stage('Back-office') {
          steps { dir('restaurantos-super-admin') { script { front() } } }
        }
      }
    }

    stage('Images') {
      when { expression { isRelease() } }
      steps {
        withCredentials([
          usernamePassword(credentialsId: 'ghcr', usernameVariable: 'GHCR_USER', passwordVariable: 'GHCR_TOKEN'),
          file(credentialsId: 'restauspace-build-env', variable: 'BUILD_ENV')
        ]) {
          sh '''
            echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
            if ! grep -q '^DOMAIN=.' "$BUILD_ENV"; then
              # Noms des variables seulement, jamais les valeurs.
              echo "restauspace-build-env : DOMAIN absent. Fichier reçu : $(wc -c < "$BUILD_ENV") octets, variables : $(grep -o '^[A-Za-z_][A-Za-z0-9_]*=' "$BUILD_ENV" | tr -d '=' | paste -sd ' ' -)" >&2
              exit 1
            fi
            cd deploy/production
            docker compose --env-file "$BUILD_ENV" -f docker-compose.build.yml build --pull
            docker compose --env-file "$BUILD_ENV" -f docker-compose.build.yml push
          '''
        }
      }
      post {
        always { sh 'docker logout ghcr.io || true' }
      }
    }

    stage('Validation') {
      when { expression { isRelease() } }
      steps {
        timeout(time: 1, unit: 'DAYS') {
          input message: "Déployer ${env.RELEASE} en production ?", ok: 'Déployer'
        }
      }
    }

    stage('Déploiement') {
      when { expression { isRelease() } }
      steps {
        // Clé SSH du VPS via Credentials Binding (pas besoin du plugin SSH Agent).
        withCredentials([sshUserPrivateKey(credentialsId: 'restauspace-vps-ssh', keyFileVariable: 'SSH_KEY', usernameVariable: 'SSH_USER')]) {
          sh '''
            [ -n "$RESTAUSPACE_DEPLOY_HOST" ] || { echo "RESTAUSPACE_DEPLOY_HOST manquant (Administrer Jenkins > Système)" >&2; exit 1; }
            case "$RESTAUSPACE_DEPLOY_HOST" in *@*) target="$RESTAUSPACE_DEPLOY_HOST" ;; *) target="$SSH_USER@$RESTAUSPACE_DEPLOY_HOST" ;; esac
            # Clé du VPS épinglée si fournie ; sinon acceptée à la première connexion seulement.
            if [ -n "${RESTAUSPACE_DEPLOY_KNOWN_HOSTS:-}" ]; then
              hosts="$(mktemp)"; trap 'rm -f "$hosts"' EXIT
              printf '%s\\n' "$RESTAUSPACE_DEPLOY_KNOWN_HOSTS" > "$hosts"
              check="-o StrictHostKeyChecking=yes -o UserKnownHostsFile=$hosts"
            else
              echo "RESTAUSPACE_DEPLOY_KNOWN_HOSTS absent : clé du VPS non épinglée" >&2
              check="-o StrictHostKeyChecking=accept-new"
            fi
            # Côté VPS, la clé n'exécute que deploy-ssh.sh, qui ne lit que le commit.
            ssh -i "$SSH_KEY" -o IdentitiesOnly=yes $check "$target" "cd ${RESTAUSPACE_DEPLOY_DIR:-restauspace/deploy/production} && ./deploy.sh $RELEASE"
          '''
        }
      }
    }
  }

  post {
    success { echo "OK : ${env.RELEASE}" }
    failure { echo "Échec : ${env.RELEASE}" }
  }
}

// Job de livraison : Pipeline simple (pas de BRANCH_NAME) construit depuis main.
def isRelease() {
  return !env.BRANCH_NAME && (env.GIT_BRANCH ?: '') ==~ /(origin\/)?main/
}

// Installation, typecheck et (hors livraison) build d'une application Next.js.
def front() {
  withEnv(["PATH+NODE=${env.JENKINS_HOME}/tools/node-v${env.NODE_VERSION}/bin"]) {
    sh 'pnpm install --frozen-lockfile'
    sh 'pnpm typecheck'
    if (!isRelease()) {
      sh 'pnpm build'
    }
  }
}
