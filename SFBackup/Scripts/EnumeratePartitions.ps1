# Connect to the cluster
Connect-ServiceFabricCluster -ConnectionEndpoint localhost:19000

# Get a list of applications on the cluster
$applications = Get-ServiceFabricApplication

$applications | foreach {
    # Get a list of stateful services running in the application
    $services = Get-ServiceFabricService -ApplicationName $_.ApplicationName
    $services | where {$_.ServiceKind -eq 'Stateful' -and $_.HasPersistedState} | foreach {
        $serviceName = $_.ServiceName
        $a = @{Expression={$serviceName};Label="ServiceName"}
        # Get a reference to the partition
        $p = Get-ServiceFabricPartition -ServiceName $serviceName
        $operationId = New-Guid
        # The call below posts a DataLoss event to the partition
        # causing its owner reliable service to call OnDataLossAsync
        # to do the state restore
        Start-ServiceFabricPartitionDataLoss -DataLossMode FullDataLoss -PartitionId $p.PartitionId -ServiceName $serviceName -OperationId $operationId
    }
}

