# ************************************************************************************
# This script is used to list the backups that currently exist for the Application
# ************************************************************************************
$clusterName = ""  #your cluster name, ie, <clustername>.<region>.cloudapp.azure.com
$certThumbprint = "" #your certificate thumbprint 
$appName = "Voting"  #The name of the service fabric application being backed up

$url = "https://" + $clusterName + ":19080/Applications/" + $appName + "/$/GetBackups?api-version=6.4"

$response = Invoke-WebRequest -Uri $url -Method Get -CertificateThumbprint $certThumbprint

$BackupPoints = (ConvertFrom-Json $response.Content)
$BackupPoints.Items