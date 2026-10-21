// CI/CD RestauSpace (job Multibranch Pipeline, Jenkins sur le Mac).
// Toutes les branches : tests .NET + vérification des 3 fronts.
// main : images poussées sur GHCR, validation manuelle, puis déploiement sur le VPS par SSH.
//
// Identifiants Jenkins attendus :
//   ghcr                  Username/password : compte GitHub + jeton (write:packages)
//   restauspace-build-env Secret file : deploy/production/build.env (voir build.env.example)
//   restauspace-vps-ssh   SSH Username with private key : compte de déploiement du VPS
// Variable globale (Administrer Jenkins > Système) : RESTAUSPACE_DEPLOY_HOST=deploy@<ip du VPS>
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

    stage('Front') {
      // Typecheck partout. Build Next.js hors main : sur main il est fait par les images.
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
      when { branch 'main' }
      steps {
        withCredentials([
          usernamePassword(credentialsId: 'ghcr', usernameVariable: 'GHCR_USER', passwordVariable: 'GHCR_TOKEN'),
          file(credentialsId: 'restauspace-build-env', variable: 'BUILD_ENV')
        ]) {
          sh '''
            echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
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
      when { branch 'main' }
      steps {
        timeout(time: 1, unit: 'DAYS') {
          input message: "Déployer ${env.RELEASE} en production ?", ok: 'Déployer'
        }
      }
    }

    stage('Déploiement') {
      when { branch 'main' }
      steps {
        sshagent(credentials: ['restauspace-vps-ssh']) {
          sh '''
            [ -n "$RESTAUSPACE_DEPLOY_HOST" ] || { echo "RESTAUSPACE_DEPLOY_HOST manquant (Administrer Jenkins > Système)" >&2; exit 1; }
            ssh -o StrictHostKeyChecking=accept-new "$RESTAUSPACE_DEPLOY_HOST" \
              "cd ${RESTAUSPACE_DEPLOY_DIR:-restauspace/deploy/production} && ./deploy.sh $RELEASE"
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

// Installation, typecheck et (hors main) build d'une application Next.js.
def front() {
  sh 'corepack pnpm install --frozen-lockfile'
  sh 'corepack pnpm typecheck'
  if (env.BRANCH_NAME != 'main') {
    sh 'corepack pnpm build'
  }
}
