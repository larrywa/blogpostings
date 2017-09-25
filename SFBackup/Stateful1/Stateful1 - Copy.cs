using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Data;
using System.IO;
using System.Fabric.Description;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO.Compression;

namespace Stateful1
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class Stateful1 : StatefulService
    {
        // When the count number gets to this value a backup is triggered
        private const int backupCount = 15;

        private IBackupStore backupManager;
        private const string CountDictionaryName = "CountDictionary";
        long incrementalCount = 0;
        private string PartitionTempDirectory;

        //Set to Azure backup or none. Disabled is the default. Overridden by config.
        private BackupManagerType backupStorageType;

        private enum BackupManagerType
        {
            Azure,
            None
        };
        private enum BackupType
        {
            Full,
            Incremental
        };
        public Stateful1(StatefulServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (e.g., HTTP, Service Remoting, WCF, etc.) for this service replica to handle client or user requests.
        /// </summary>
        /// <remarks>
        /// For more information on service communication, see http://aka.ms/servicefabricservicecommunication
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new ServiceReplicaListener[0];
        }

        /// <summary>
        /// This is the main entry point for your service replica.
        /// This method executes when this replica of your service becomes primary and has write status.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {

            IReliableDictionary<string,long> myDictionary = 
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>
                (CountDictionaryName);

            bool takeBackup = false;
            bool takeFullBackup = false;
            

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var tx = this.StateManager.CreateTransaction())
                {
                    var result = await myDictionary.TryGetValueAsync(tx, "Counter");

                    if (result.HasValue)
                    {
                        ServiceEventSource.Current.ServiceMessage(this, "Current Counter Value: {0}", result.Value.ToString());
                    }
                    else
                    {
                        ServiceEventSource.Current.ServiceMessage(this, "Value does not exist and will be added to the reliable dictionary...");
                    }
                    
                    // Setting a flag that will be true when the counter in the reliable dictionary
                    // hits a multiple of 100
                    long newCount = await myDictionary.AddOrUpdateAsync(tx, "Counter", 0, 
                        (key, value) => ++value);
                    takeBackup = newCount > 0 && newCount % backupCount == 0;

                    // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                    // discarded, and nothing is saved to the secondary replicas.
                    await tx.CommitAsync();
                }


                // If the backup flag was set, then take a backup of this service's state
                if (takeBackup)
                {
                    
                    ServiceEventSource.Current.ServiceMessage(this, "Backup initiated...");

                    // NOTE
                    // Here you could have logic to change the type of backup, full or incremental
                    // Indicate that we want a full backup, and to call BackupCallbackAsync when the backup is complete

                    if (!takeFullBackup)
                    {
                        BackupDescription backupDescription = new BackupDescription(BackupOption.Full,
                          this.BackupCallbackAsync);
                        incrementalCount = 0;
                        await base.BackupAsync(backupDescription);
                        takeFullBackup = true;
 
                    }
                    else
                    {
                        try
                        {
                            BackupDescription backupDescription = new BackupDescription(BackupOption.Incremental,
          this.BackupCallbackAsync);

                            // Call BackupAsync, which is implemented in StatefulServiceBase (not in this code). 
                            // Calling it prompts Service Fabric to do the backup you requested.
                            // All reliable objects are collected
                            // The BackupDescription object created above tells it what kind of backup and where to call
                            // this code back with status
                            incrementalCount++;
                            await base.BackupAsync(backupDescription);
                            
                        }
                        catch (System.Exception ee)
                        {

                        }
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }

        // BackupCallbackAsync is an arbitrary name, you can name it anything you want, but just make sure
        // that you specify it in the BackupDescription object you created (please see a few lines above here).
        // This is called AFTER Service Fabric has completed your backup.The backup is written by Service Fabric 
        // to an internal directory. The method below moves the backup files to blob storage by use of 
        // AzureBackupManager class. It is just an illustration of how to get the
        // backup files to a place you maintain and of course you would not want to hard code it.
        // If you return false from this callback, you'll indicate to Service Fabric that the backup was not successful.
        private async Task<bool> BackupCallbackAsync(BackupInfo backupInfo, 
            CancellationToken cancellationToken)
        {
            this.SetupBackupManager();
            ServiceEventSource.Current.ServiceMessage(this, "Inside backup callback for replica {0}|{1}", 
                this.Context.PartitionId, this.Context.ReplicaId);

            IReliableDictionary<string, long> backupCountDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>(CountDictionaryName);

            try
            {
                ServiceEventSource.Current.ServiceMessage(this, "Archiving backup");
                await this.backupManager.ArchiveBackupAsync(backupInfo, cancellationToken);
                ServiceEventSource.Current.ServiceMessage(this, "Backup archived");

                //only delete backups after a full backup
                if (incrementalCount == 0)
                {
                    await this.backupManager.DeleteBackupsAsyncIncremental(cancellationToken);
                }

                ServiceEventSource.Current.Message("Backups deleted");
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, 
                    "Archive of backup failed: Source: {0} Exception: {1}", 
                    backupInfo.Directory, e.Message);
            }

            return true;
        }

        // OnDataLossAsync is the restore part of the process. This is NOT an arbitrary name, it is an override of the 
        // method on StatefulServiceBase.
        // The method below looks in the c:\temp\sfbackup directory (where I stored the backup files in BackupCallbackAsync
        // above), searches for the folder of backup files that have the newest ListWriteTime, then restore them.
        // Once the RestoreDescription object is told where to find your data, RestoreAsync is called
        // to restore that data to the reliable objects
        protected override async Task<bool> OnDataLossAsync(RestoreContext restoreCtx, 
            CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.ServiceMessage(this, "OnDataLoss Invoked!");
            this.SetupBackupManager();

            try
            {
                string restorePath = "";

                if (this.backupStorageType == BackupManagerType.None)
                {
                    //since we have no backup configured, we return false to indicate
                    //that state has not changed. This replica will become the basis
                    //for future replica builds
                    return false;
                }
                else
                {
                    string zipPath = "";
                    
                    string partitionId = this.Context.PartitionId.ToString("N");

                    //this will be the root directory for all unzipped files
                    ICodePackageActivationContext codePackageContext = this.Context.CodePackageActivationContext;
                    //this.PartitionTempDirectory = Path.Combine(codePackageContext.TempDirectory, partitionId);
                    this.PartitionTempDirectory = Path.Combine(@"C:\myrestore", partitionId);


                    // Get back the full list of blobs and do not sort them
                    // Sorting will be used when restoring a full-only backup
                    IEnumerable<IListBlobItem> blobs = await this.backupManager.GetBackupBlobs(false);

                    // the partition temp directory is the root restore directory + partitionId
                    // create this directory
                    System.IO.Directory.CreateDirectory(this.PartitionTempDirectory);


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
                        }
                        catch (System.Exception ee)
                        {

                        }

                        string restoreDir = (backup.Name).Remove(backup.Name.Length - 4);

                        // Create a path in which to unzip the zip file that has been downloaded
                        // to the partiton directory
                        //restorePath = Path.Combine(this.PartitionTempDirectory, partitionId);
                        restorePath = Path.Combine(this.PartitionTempDirectory, restoreDir);

                        //extract the zip in to the local temp path directory
                        ZipFile.ExtractToDirectory(zipPath, restorePath);


                    }

                    //clean up the zip file, files are now in the restore path
                    FileInfo zipInfo = new FileInfo(zipPath);
                    zipInfo.Delete();

                    //backupFolder = 
                    //    await this.backupManager.RestoreLatestBackupToTempLocationIncremental(cancellationToken);
                }

                ServiceEventSource.Current.ServiceMessage(this, "Restoration Folder Path " + restorePath);

                RestoreDescription restoreDescription = 
                    new RestoreDescription(this.PartitionTempDirectory, RestorePolicy.Force);

                // Restore the backup copy
                await restoreCtx.RestoreAsync(restoreDescription, cancellationToken);

                ServiceEventSource.Current.ServiceMessage(this, "Restore completed");

                // Clean up the local temporary directory
                DirectoryInfo tempRestoreDirectory = new DirectoryInfo(this.PartitionTempDirectory);
                tempRestoreDirectory.Delete(true);

                return true;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Restoration failed: " + "{0} {1}" + e.GetType() + e.Message);

                throw;
            }
        }
        private void SetupBackupManager()
        {
            string partitionId = this.Context.PartitionId.ToString("N");
            long minKey = ((Int64RangePartitionInformation)this.Partition.PartitionInfo).LowKey;
            long maxKey = ((Int64RangePartitionInformation)this.Partition.PartitionInfo).HighKey;

            if (this.Context.CodePackageActivationContext != null)
            {
                ICodePackageActivationContext codePackageContext = this.Context.CodePackageActivationContext;
                ConfigurationPackage configPackage = codePackageContext.GetConfigurationPackageObject("Config");
                ConfigurationSection configSection = configPackage.Settings.Sections["Stateful1.Settings"];

                string backupSettingValue = configSection.Parameters["BackupMode"].Value;

                if (string.Equals(backupSettingValue, "none", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.None;
                }
                else if (string.Equals(backupSettingValue, "azure", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.Azure;

                    ConfigurationSection azureBackupConfigSection = 
                        configPackage.Settings.Sections["Stateful1.BackupSettings.Azure"];

                    this.backupManager = new AzureBlobBackupManager(azureBackupConfigSection, 
                        partitionId, minKey, maxKey, codePackageContext.TempDirectory);
                }
                else
                {
                    throw new ArgumentException("Unknown backup type");
                }

                ServiceEventSource.Current.ServiceMessage(this, "Backup Manager Set Up");
            }
        }
    }
}
