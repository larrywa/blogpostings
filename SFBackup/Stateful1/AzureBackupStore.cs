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

        private string PartitionTempDirectory;
        private string partitionId;

        private long keyMin;
        private long keyMax;
        private string tempRestoreDir;
        private string zipPath;
        private string restorePath;

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

            this.partitionId = partitionId;

            // This is the directory on your nodes hard drive that will be used to download
            // the files needed to do the restore. Don't put your directory structure too deep
            // otherwise you could end up with path length limitations
            tempRestoreDir = configSection.Parameters["TempRestoreDirectory"].Value;

            // This directory is the root directory that all zip files will be download in to 
            // and extracted to, ie, the root folder under the folder where all your restores are performed
            this.PartitionTempDirectory = Path.Combine(tempRestoreDir, partitionId);
            
            StorageCredentials storageCredentials = new StorageCredentials(backupAccountName, backupAccountKey);
            this.cloudBlobClient = new CloudBlobClient(new Uri(blobEndpointAddress), storageCredentials);
            this.backupBlobContainer = this.cloudBlobClient.GetContainerReference(this.partitionId);
            this.backupBlobContainer.CreateIfNotExists();
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

            // the fullArchiveDirectory is the local directory where you will be
            // storing the backup data until you push it up in to Azure blob storage
            string fullArchiveDirectory = Path.Combine(this.PartitionTempDirectory, 
                Guid.NewGuid().ToString("N"));

            DirectoryInfo fullArchiveDirectoryInfo = new DirectoryInfo(fullArchiveDirectory);
            fullArchiveDirectoryInfo.Create();

            //generate a random number for blob name uniqueness
            // come up with your own logic here, however you want to name these blobs
            Random random = new Random();
            long num = random.Next();

            string blobName = string.Format("{0}_{1}_{2}_{3}", 
                Guid.NewGuid().ToString("N"), 
                this.keyMin, 
                num, 
                "Backup.zip");

            // Get the name of the blob
            CloudBlockBlob blob = this.backupBlobContainer.GetBlockBlobReference(blobName);

            // add .Backup.zip to the end of it
            string fullArchivePath = Path.Combine(fullArchiveDirectory, "Backup.zip");

            try
            {
                // Create a zip file format
                ZipFile.CreateFromDirectory(backupInfo.Directory, 
                fullArchivePath, 
                CompressionLevel.Fastest, false);


                DirectoryInfo backupDirectory = new DirectoryInfo(backupInfo.Directory);
                backupDirectory.Delete(true);
            }
            catch(System.Exception ee)
            {
                ServiceEventSource.Current.Message("ArchiveBackupAsync:Deleting zip file backup directory failed with {0} ", ee.Message);
                throw;
            }


            // NOTE
            // If you go above NuGet package version 6.1.1 for WindowsAzure.Storage, you will see that you
            // need an extra paramete
            try
            {
                await blob.UploadFromFileAsync(fullArchivePath, FileMode.Open, CancellationToken.None);
            
                DirectoryInfo tempDirectory = new DirectoryInfo(fullArchiveDirectory);
                
                tempDirectory.Delete(true);
            }
            catch (System.Exception ee)
            {
                ServiceEventSource.Current.Message("ArchiveBackupAsync: Uploading file to blob storage failed with {0} ", ee.Message);
                throw;
            }
            ServiceEventSource.Current.Message("AzureBlobBackupManager: UploadBackupFolderAsync: success.");
        }

        /// <summary>
        /// When restoring the data from the storage account, put the data in to a temporary
        /// directory on the machine. This method downloads each blob zip file that is a part of the 
        /// backup (full or incremental) and unzips the file in to the partition directory under the
        /// restore location
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<string> RestoreLatestBackupToTempLocation(CancellationToken cancellationToken)
        {

            ServiceEventSource.Current.Message("AzureBlobBackupManager: RestoreLatestBackupToTempLocation called.");

            // Get back the full list of blobs and do not sort them
            // Sorting will be used when restoring a full-only backup
            IEnumerable<IListBlobItem> blobs = await this.GetBackupBlobs(false);

            foreach (CloudBlockBlob backup in blobs)
            {
                // this is the full zip path including file name and extension
                // this is the location the zip file should be downloaded to
                zipPath = Path.Combine(this.PartitionTempDirectory, backup.Name);

                // download the zip file from storage
                try
                {
                    // download the file in to the partition directory
                    backup.DownloadToFile(zipPath, FileMode.CreateNew);
                

                string restoreDir = (backup.Name).Remove(backup.Name.Length - 4);

                // Create a path in which to unzip the zip file that has been downloaded
                // to the partiton directory
                restorePath = Path.Combine(this.PartitionTempDirectory, restoreDir);

                //extract the zip in to the local temp path directory
                ZipFile.ExtractToDirectory(zipPath, restorePath);
                }
                catch (System.Exception ee)
                {
                    ServiceEventSource.Current.Message("RestoreLatestBackupToTempLocation: Downloading backup zip file failed with {0} ", ee.Message);
                    throw;
                }
            }

            try
            {
                // delete the zip file that was downloaded to the local directory
                FileInfo zipInfo = new FileInfo(zipPath);
                zipInfo.Delete();
            }
            catch(System.Exception aa)
            {
                ServiceEventSource.Current.Message("RestoreLatestBackupToTempLocation: Deletion of zip file from restore folder failed with {0} ", aa.Message);
                throw;
            }

            return this.PartitionTempDirectory;
        }
        /// <summary>
        /// Deletes any of the old/previous backup files from the storage account container
        /// You want to make sure that whatever is in your container are ONLY the files needed for
        /// the next restore
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task DeleteBackupsAsync(CancellationToken cancellationToken)
        {
            if (this.backupBlobContainer.Exists())
            {
                ServiceEventSource.Current.Message("AzureBlobBackupManager: Deleting old backups");

                IEnumerable<CloudBlockBlob> oldBackups = (await this.GetBackupBlobs(true)).Skip(1);

                foreach (CloudBlockBlob backup in oldBackups)
                {
                    ServiceEventSource.Current.Message("AzureBlobBackupManager: Deleting {0}", backup.Name);
                    try
                    {
                        await backup.DeleteAsync(cancellationToken);
                    }
                    catch (System.Exception delee)
                    {
                        ServiceEventSource.Current.Message("DeleteBackupAsyncIncremental: Deleting old backup files failed with {0} ", delee.Message);
                        throw;
                    }
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
        
    }
}