﻿# ************************************************************************************
# This script is used to create a backup policy and then enabled the periodic backup policy
# ************************************************************************************

$appName = 'Voting' #The name of the service fabric application being backed up
$storageConnString = '' #Your storage account connection string
$backupPolicyName = "myvotebackup" #The name of your backup policy
$clusterName = '' #your cluster name, ie, <clustername>.<region>.cloudapp.azure.com
$certThumbprint = '' #your certificate thumbprint
$containerName = '' #The name of the storage account container your backup data will be located in


#start setting up storage info
$StorageInfo = @{
    ConnectionString = $storageConnString
    ContainerName = $containerName
    StorageKind = 'AzureBlobStore'
}

# backup schedule info, backup every 10 minutes
$ScheduleInfo = @{
    Interval = 'PT5M'
    ScheduleKind = 'FrequencyBased'
}

$retentionPolicy = @{
    RetentionPolicyType = 'Basic'
    RetentionDuration = 'P10D'
}

# backup policy parameters
# After 5 incremental backups, do a full backup
$BackupPolicy = @{
    Name = $backupPolicyName
    MaxIncrementalBackups = 5
    Schedule = $ScheduleInfo
    Storage = $StorageInfo
    RetentionPolicy = $retentionPolicy
    
}

$body = (ConvertTo-Json $BackupPolicy)

#===========================================
# Create the Backup policy
#===========================================
$url = "https://" + $clusterName + ":19080/BackupRestore/BackupPolicies/$/Create?api-version=6.4"


#===========================================
# The current requirements of backup-restore require valid certificates that have a root
# For lab purposes, we are creating a self-signed cert with no root so we need to disable
# the confirmation process for checking for root authority
# If you have a valid cert
#===========================================
if (-not($validCert))
{
    if (-not("dummy" -as [type])) {
        add-type -TypeDefinition @"
    using System;
    using System.Net;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;

    public static class Dummy {
        public static bool ReturnTrue(object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors) { return true; }

        public static RemoteCertificateValidationCallback GetDelegate() {
            return new RemoteCertificateValidationCallback(Dummy.ReturnTrue);
        }
    }
"@
    }

    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = [dummy]::GetDelegate()
}


#===========================================
# Invoke to create the backup policy
#===========================================
Invoke-WebRequest -Uri $url -Method Post -Body $body -ContentType 'application/json' -CertificateThumbprint $certThumbprint -Debug -Verbose



$BackupPolicyReference = @{
    BackupPolicyName = $backupPolicyName
}

$body = (ConvertTo-Json $BackupPolicyReference)

#===========================================
#Enable periodic backup of the Voting service
#===========================================
$url = "https://" + $clusterName + ":19080/Applications/" + $appName + "/$/EnableBackup?api-version=6.4"

Invoke-WebRequest -Uri $url -Method Post -Body $body -ContentType 'application/json' -CertificateThumbprint $certThumbprint

