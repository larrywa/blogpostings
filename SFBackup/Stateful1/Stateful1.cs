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
        private bool takeFullBackup;

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
            takeFullBackup = true;

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
                    
                    long newCount = await myDictionary.AddOrUpdateAsync(tx, "Counter", 0, 
                        (key, value) => ++value);

                    // calculate whether to do a backup based on the current count
                    takeBackup = newCount > 0 && newCount % backupCount == 0;

                    // If an exception is thrown before calling CommitAsync, the transaction aborts, all changes are 
                    // discarded, and nothing is saved to the secondary replicas.
                    await tx.CommitAsync();
                }


                // If the backup flag was set, then take a backup of this service's state
                if (takeBackup)
                {
                    
                    // NOTE
                    // Here you could have logic to change the type of backup, full or incremental
                    // Indicate that we want a full backup, and to call BackupCallbackAsync when the backup is complete

                    if (takeFullBackup)
                    {
                        ServiceEventSource.Current.ServiceMessage(this, "Full Backup initiated...");
                        BackupDescription backupDescription = new BackupDescription(BackupOption.Full,
                          this.BackupCallbackAsync);

                            // Make sure to set incremental counter back to zero
                            incrementalCount = 0;

                            // Call BackupAsync, which is implemented in StatefulServiceBase (not in this code). 
                            // Calling it prompts Service Fabric to do the backup you requested.
                            // All reliable objects are collected
                            // The BackupDescription object created above tells it what kind of backup and where to call
                            // this code back with status
                            await base.BackupAsync(backupDescription);

                            // In this sample code, set takeFullBackup to False because the next backup
                            // will be incremental. You need to change this logic for your own use to decide
                            // when you want full backups taken
                            takeFullBackup = false;
 
                    }
                    else
                    {
                        try
                        {
                            ServiceEventSource.Current.ServiceMessage(this, "Incremental Backup initiated...");

                            BackupDescription backupDescription = new BackupDescription(BackupOption.Incremental,
          this.BackupCallbackAsync);

                            // increment the incremental count prior to calling BackupAsync
                            incrementalCount++;
                            await base.BackupAsync(backupDescription);
                            
                        }
                        catch (System.Exception ee)
                        {
                            ServiceEventSource.Current.ServiceMessage(this, 
                                "Exception throw during incremental backup : {0}",ee.Message);

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

                //only delete backups that remain in your blob folder after a full backup
                // Do this in case the same partition starts a new backup process, just need
                // to start with a clean blob partition folder
                if (incrementalCount == 0)
                {
                    await this.backupManager.DeleteBackupsAsync(cancellationToken);
                    ServiceEventSource.Current.Message("Old Backup files deleted");
                }

                
            }
            catch (Exception ee)
            {
                ServiceEventSource.Current.ServiceMessage(this, 
                    "Archive of backup failed: Source: {0} Exception: {1}", 
                    backupInfo.Directory, ee.Message);
            }

            return true;
        }

        // OnDataLossAsync is the restore part of the process. This is NOT an arbitrary name, it is an override of the 
        // method on StatefulServiceBase.
        // The method below looks in the backupFolder directory (where the backup files were
        // stored in BackupCallbackAsync above), searches for the folder of backup files that have the newest ListWriteTime, then restores them.
        // Once the RestoreDescription object is told where to find your data, RestoreAsync is called
        // to restore that data to the reliable objects
        // Each partition will have OnDataLossAsync called on it
        protected override async Task<bool> OnDataLossAsync(RestoreContext restoreCtx, 
            CancellationToken cancellationToken)
        {
            string backupFolder;

            ServiceEventSource.Current.ServiceMessage(this, "OnDataLoss Invoked!");

            this.SetupBackupManager();
            
            try
            {

                if (this.backupStorageType == BackupManagerType.None)
                {
                    //since we have no backup configured, we return false to indicate
                    //that state has not changed. This replica will become the basis
                    //for future replica builds
                    return false;
                }
                else
                {
                    backupFolder = 
                        await this.backupManager.RestoreLatestBackupToTempLocation(cancellationToken);
                }


                ServiceEventSource.Current.ServiceMessage(this, "Restoration Folder Path " + backupFolder);

                RestoreDescription restoreDescription = 
                    new RestoreDescription(backupFolder, RestorePolicy.Force);

                // Restore the backup copy
                await restoreCtx.RestoreAsync(restoreDescription, cancellationToken);

                ServiceEventSource.Current.ServiceMessage(this, "Restore completed");

                // Clean up the local temporary directory (the root directory where all backups were unzipped in to)
                try
                {
                    DirectoryInfo tempRestoreDirectory = new DirectoryInfo(backupFolder);
                    tempRestoreDirectory.Delete(true);
                }
                catch(System.Exception ddel)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "OnDataLossAsync: Delete of backup folder failed with {0}", ddel.Message);
                }

                // now that a restore has been performed, reset the full backup flag and incremental
                // counter so you start fresh again
                // You will need to change this type of logic for your own scenario
                takeFullBackup = true;
                incrementalCount = 0;

                return true;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Restoration failed: " + "{0} {1}" + e.GetType() + e.Message);
                throw;
            }
        }
        /// <summary>
        /// This method is used to create an instance of the AzureBlobBackupManager
        /// </summary>
        private void SetupBackupManager()
        {
            // change the logic here for your own partition scenario
            // 
            string partitionId = this.Context.PartitionId.ToString("N");
            long minKey = ((Int64RangePartitionInformation)this.Partition.PartitionInfo).LowKey;
            long maxKey = ((Int64RangePartitionInformation)this.Partition.PartitionInfo).HighKey;

            if (this.Context.CodePackageActivationContext != null)
            {
                ICodePackageActivationContext codePackageContext = 
                    this.Context.CodePackageActivationContext;

                ConfigurationPackage configPackage = 
                    codePackageContext.GetConfigurationPackageObject("Config");

                // change the section name to match your service name
                ConfigurationSection configSection = 
                    configPackage.Settings.Sections["Stateful1.Settings"];

                string backupSettingValue = configSection.Parameters["BackupMode"].Value;

                if (string.Equals(backupSettingValue, "none", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.None;
                }
                else if (string.Equals(backupSettingValue, "azure", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.Azure;

                    // change the section name to match your service name
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
