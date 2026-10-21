pipeline{
    agent any
    // build stage
    stages{
        stage('Build'){
            steps{
                echo 'Building...'
                sh 'dotnet build'
            }
        }
        stage('Test'){
            steps{
                echo 'Testing...'
                sh 'dotnet test'
            }
        }
    }
}