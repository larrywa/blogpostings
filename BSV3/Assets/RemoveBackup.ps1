# ************************************************************************************
# The purpose of this script is to remove a backup (but not the data) that is associated with # an application
# ************************************************************************************


$appName = "Voting"  #The name of the service fabric application being backed up
$clusterName = ""  #your cluster name, ie, <clustername>.<region>.cloudapp.azure.com
$certThumbprint = "" #your certificate thumbprint 
$backupPolicyName = "" #The name of your backup policy

#====================================
#Suspend Application Backup
#====================================
$body = ""
$url = "https://" + $clusterName + ":19080/Applications/" + $appName + "/$/SuspendBackup?api-version=6.4"


Invoke-WebRequest -Uri $url -Method Post -Body $body -ContentType 'application/json' -CertificateThumbprint $certThumbprint

#====================================
# Disable the backup
# A body is not required to disable a backup. In the previous suspend call the body was set to ""
#====================================
$url = "https://" + $clusterName + ":19080/Applications/" + $appName + "/$/DisableBackup?api-version=6.4"


Invoke-WebRequest -Uri $url -Method Post -Body $body -ContentType 'application/json' -CertificateThumbprint $certThumbprint 

#====================================
#Delete the backup policy
#====================================

$body = ""
$url = "https://" + $clusterName + ":19080/BackupRestore/BackupPolicies/" + $backupPolicyName + "/$/Delete?api-version=6.4"


Invoke-WebRequest -Uri $url -Method Post -Body $body -ContentType 'application/json' -CertificateThumbprint $certThumbprint
