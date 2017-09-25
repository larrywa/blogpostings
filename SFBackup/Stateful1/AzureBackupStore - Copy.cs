// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Stateful1
{
    using System;
    using System.Collections.Generic;
    using System.Fabric.Description;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.WindowsAzure.Storage.Auth;
    using Microsoft.WindowsAzure.Storage.Blob;

    public class AzureBlobBackupManager : IBackupStore
    {
        private readonly CloudBlobClient cloudBlobClient;
        private CloudBlobContainer backupBlobContainer;
        private int MaxBackupsToKeep;

        private string PartitionTempDirectory;
        private string partitionId;

        private long backupFrequencyInSeconds;
        private long keyMin;
        private long keyMax;

        /// <summary>
        /// Pull in Settings.xml values and connect to storage
        /// </summary>
        /// <param name="configSection"></param>
        /// <param name="partitionId"></param>
        /// <param name="keymin"></param>
        /// <param name="keymax"></param>
        /// <param name="codePackageTempDirectory"></param>
        public AzureBlobBackupManager(ConfigurationSection configSection, 
            string partitionId, 
            long keymin, 
            long keymax, 
            string codePackageTempDirectory)
        {
            this.keyMin = keymin;
            this.keyMax = keymax;

            string backupAccountName = configSection.Parameters["BackupAccountName"].Value;
            string backupAccountKey = configSection.Parameters["PrimaryKeyForBackupTestAccount"].Value;
            string blobEndpointAddress = configSection.Parameters["BlobServiceEndpointAddress"].Value;

            this.backupFrequencyInSeconds = long.Parse(configSection.Parameters["BackupFrequencyInSeconds"].Value);
            this.MaxBackupsToKeep = int.Parse(configSection.Parameters["MaxBackupsToKeep"].Value);
            this.partitionId = partitionId;
            this.PartitionTempDirectory = Path.Combine(codePackageTempDirectory, partitionId);

            StorageCredentials storageCredentials = new StorageCredentials(backupAccountName, backupAccountKey);
            this.cloudBlobClient = new CloudBlobClient(new Uri(blobEndpointAddress), storageCredentials);
            this.backupBlobContainer = this.cloudBlobClient.GetContainerReference(this.partitionId);
            this.backupBlobContainer.CreateIfNotExists();
        }

        long IBackupStore.backupFrequencyInSeconds
        {
            get { return this.backupFrequencyInSeconds; }
        }

        /// <summary>
        /// Send the backup to blob storage
        /// </summary>
        /// <param name="backupInfo"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ArchiveBackupAsync(BackupInfo backupInfo, CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("AzureBlobBackupManager: Archive Called.");

            string fullArchiveDirectory = Path.Combine(this.PartitionTempDirectory, Guid.NewGuid().ToString("N"));

            DirectoryInfo fullArchiveDirectoryInfo = new DirectoryInfo(fullArchiveDirectory);
            fullArchiveDirectoryInfo.Create();

            string blobName = string.Format("{0}_{1}_{2}_{3}", Guid.NewGuid().ToString("N"), this.keyMin, this.keyMax, "Backup.zip");
            string fullArchivePath = Path.Combine(fullArchiveDirectory, "Backup.zip");
            
            // Create a zip file format
            ZipFile.CreateFromDirectory(backupInfo.Directory, fullArchivePath, CompressionLevel.Fastest, false);

            DirectoryInfo backupDirectory = new DirectoryInfo(backupInfo.Directory);
            backupDirectory.Delete(true);

            CloudBlockBlob blob = this.backupBlobContainer.GetBlockBlobReference(blobName);

            // NOTE
            // If you go above NuGet package version 6.1.1 for WindowsAzure.Storage, you will see that you
            // need an extra parameter
            await blob.UploadFromFileAsync(fullArchivePath, FileMode.Open, CancellationToken.None);
            
            DirectoryInfo tempDirectory = new DirectoryInfo(fullArchiveDirectory);
            tempDirectory.Delete(true);

            ServiceEventSource.Current.Message("AzureBlobBackupManager: UploadBackupFolderAsync: success.");
        }

        /// <summary>
        /// When restoring the data from the storage account, put the data in to a temporary
        /// directory on the machine
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<string> RestoreLatestBackupToTempLocation(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("AzureBlobBackupManager: Download backup async called.");

            CloudBlockBlob lastBackupBlob = (await this.GetBackupBlobs(true)).First();

            ServiceEventSource.Current.Message("AzureBlobBackupManager: Downloading {0}", lastBackupBlob.Name);

            // NOTE
            // Here, depending on your partitioning logic, you will need to have different logic
            string downloadId = Guid.NewGuid().ToString("N");

            string zipPath = Path.Combine(this.PartitionTempDirectory, 
                string.Format("{0}_Backup.zip", downloadId));

            lastBackupBlob.DownloadToFile(zipPath, FileMode.CreateNew);

            string restorePath = Path.Combine(this.PartitionTempDirectory, downloadId);

            ZipFile.ExtractToDirectory(zipPath, restorePath);

            FileInfo zipInfo = new FileInfo(zipPath);
            zipInfo.Delete();

            ServiceEventSource.Current.Message("AzureBlobBackupManager: Downloaded {0} in to {1}", 
                lastBackupBlob.Name, restorePath);

            return restorePath;
        }
        public async Task<string> RestoreLatestBackupToTempLocationIncremental(
            CancellationToken cancellationToken)
        {
            string zipPath = "";
            string restorePath = "";

            ServiceEventSource.Current.Message("AzureBlobBackupManager: Download backup async called.");

            // Get back the full list of blobs and do not sort them
            // Sorting will be used when restoring a full-only backup
            IEnumerable<IListBlobItem> blobs = await this.GetBackupBlobs(false);

            //ServiceEventSource.Current.Message("AzureBlobBackupManager: Downloading {0}", lastBackupBlob.Name);

            // NOTE
            // Here, depending on your partitioning logic, you will need to have different logic
            string downloadId = Guid.NewGuid().ToString("N");

            foreach (CloudBlockBlob backup in blobs)
            {
                
                //build the zip path
                zipPath = Path.Combine(this.PartitionTempDirectory,
                    string.Format("{0}_Backup.zip", downloadId));

                // download the zip file from storage
                try
                {
                    backup.DownloadToFile(zipPath, FileMode.CreateNew);
                }
                catch(System.Exception ee)
                {

                }
                restorePath = Path.Combine(this.PartitionTempDirectory, downloadId);

                //extract the zip in to the local temp path directory
                ZipFile.ExtractToDirectory(zipPath, restorePath);


            }

            //clean up the zip folder, files are now in the restore path
            FileInfo zipInfo = new FileInfo(zipPath);
            zipInfo.Delete();

            //ServiceEventSource.Current.Message("AzureBlobBackupManager: Downloaded {0} in to {1}",
            //    lastBackupBlob.Name, restorePath);

            return restorePath;
        }

        /// <summary>
        /// In Settings.xml there is a setting 'MaxBackupsToKeep' used to determine how many backups
        /// you want to hang on too and then delete the older ones
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task DeleteBackupsAsyncIncremental(CancellationToken cancellationToken)
        {
            if (this.backupBlobContainer.Exists())
            {
                ServiceEventSource.Current.Message("AzureBlobBackupManager: Deleting old backups");

                IEnumerable<CloudBlockBlob> oldBackups = (await this.GetBackupBlobs(true)).Skip(1);

                foreach (CloudBlockBlob backup in oldBackups)
                {
                    ServiceEventSource.Current.Message("AzureBlobBackupManager: Deleting {0}", backup.Name);
                    await backup.DeleteAsync(cancellationToken);
                }
            }
        }
        public async Task DeleteBackupsAsync(CancellationToken cancellationToken)
        {
            if (this.backupBlobContainer.Exists())
            {
                ServiceEventSource.Current.Message("AzureBlobBackupManager: Deleting old backups");

                IEnumerable<CloudBlockBlob> oldBackups = (await this.GetBackupBlobs(true)).Skip(this.MaxBackupsToKeep);

                foreach (CloudBlockBlob backup in oldBackups)
                {
                    ServiceEventSource.Current.Message("AzureBlobBackupManager: Deleting {0}", backup.Name);
                    await backup.DeleteAsync(cancellationToken);
                }
            }
        }
        /// <summary>
        /// Method used by DeleteBackupsAsync to get the list of backup blobs
        /// The blobs are then sorted and then the oldest is returned from this method
        /// </summary>
        /// <param name="sorted"></param>
        /// <returns></returns>
        public async Task<IEnumerable<CloudBlockBlob>> GetBackupBlobs(bool sorted)
        {
            IEnumerable<IListBlobItem> blobs = this.backupBlobContainer.ListBlobs();

            ServiceEventSource.Current.Message("AzureBlobBackupManager: Got {0} blobs", blobs.Count());

            List<CloudBlockBlob> itemizedBlobs = new List<CloudBlockBlob>();

            foreach (CloudBlockBlob cbb in blobs)
            {
                await cbb.FetchAttributesAsync();
                itemizedBlobs.Add(cbb);
            }

            if (sorted)
            {
                return itemizedBlobs.OrderByDescending(x => x.Properties.LastModified);
            }
            else
            {
                return itemizedBlobs;
            }
        }
        private async Task<IEnumerable<CloudBlockBlob>> GetBackupBlobsForIncremental(bool sorted)
        {
            IEnumerable<IListBlobItem> blobs = this.backupBlobContainer.ListBlobs();

            ServiceEventSource.Current.Message("AzureBlobBackupManager: Got {0} blobs", blobs.Count());

            List<CloudBlockBlob> itemizedBlobs = new List<CloudBlockBlob>();

            foreach (CloudBlockBlob cbb in blobs)
            {
                await cbb.FetchAttributesAsync();
                itemizedBlobs.Add(cbb);
            }

            if (sorted)
            {
                return itemizedBlobs.OrderByDescending(x => x.Properties.LastModified);
            }
            else
            {
                return itemizedBlobs;
            }
        }
    }
}