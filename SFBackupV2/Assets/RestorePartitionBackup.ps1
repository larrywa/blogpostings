# ************************************************************************************
# The purpose of this script is to restore a specific backup to a specific partition
# You will need to:
# 1. BackupID - you'll need to have an idea of what time/date the backup took place. 
#    Then you'll run the ListBackups.ps1  to get the backup information
# 2. Know which partition you are storing to
# 3. The location you backup is at, in your storage account. Obtained from ListBackups.ps1
# 4. Backup container name
# 5. Storage account connection string
# 6.  - obtained from running ListBackups.ps1
# ************************************************************************************


$partitionId = ""  #The partition ID you want to restore
$backupId = ''  #the BackupID obtained from either viewing your storage account backup item or running ListPartitionBackups
$storageConnString = "" #Your storage account connection string
$containerName = "" #The name of the storage account container your backup data will be located in
$backupLocation = "Voting\\VotingData\\<your-partitionId>\\<your-zip-file-name>" #the location of the zip file in your storage container, obtained from ListBackups

#================================================
# Restore Backup
# The key to successful JSON construction is to
# to make sure you have "" around each property
#================================================

$body = @"
{
  "BackupId": "$backupId",
  "BackupStorage": {
    "StorageKind": "AzureBlobStore",
    "ConnectionString": "$storageConnString",
    "ContainerName": "$containerName"
  },
  "BackupLocation": "$backupLocation"
}
"@

$url = "https://" + $clusterName + ":19080/Partitions/" + $partitionId + "/$/Restore?api-version=6.4"


Invoke-WebRequest -Uri $url -Method Post -Body $body -ContentType 'application/json' -CertificateThumbprint $certThumbprint -Verbose

