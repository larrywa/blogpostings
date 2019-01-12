# ************************************************************************************
# This script is used to list a particular partitions backups
# ************************************************************************************
$clusterName = ""  #your cluster name, ie, <clustername>.<region>.cloudapp.azure.com
$partitionId = ""  #your stateful service partition ID
$certThumbprint = "" #your certificate thumbprint 
$appName = "Voting"  #The name of the service fabric application being backed up

$url = "https://" + $clusterName + ":19080/Partitions/" + $partitionId + "/$/GetBackups?api-version=6.4"

$response = Invoke-WebRequest -Uri $url -Method Get -CertificateThumbprint $certThumbprint

$BackupPoints = (ConvertFrom-Json $response.Content)
$BackupPoints.Items