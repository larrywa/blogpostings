#-----You have to change the variable values-------#

#Name of the Key Vault service
# No capital letters!
$KeyVaultName = "[your-vault-name]" 
#Resource group for the Key-Vault service. 
$ResourceGroup = "[resource-group-where-your-vault-will-be]"
#Set the Subscription
$subscriptionId = "[your-Azure-Subscription-ID]" 
#Azure data center locations (East US", "West US" etc)
$Location = "[your-region]"
#Password for the certificate
$Password = "[certificate-password]"
#DNS name for the certificate
$CertDNSName = "[name-of-certificate-not-including-.pfx]"
#Name of the secret in key vault
$KeyVaultSecretName = "[key-vault-secret-name-for-cert]"
#Path to directory on local disk in which the certificate is stored  
$CertFileFullPath = "C:\Certs\$CertDNSName.pfx"

#If more than one under your account
Select-AzureRmSubscription -SubscriptionId $subscriptionId
#Verify Current Subscription
Get-AzureRmSubscription –SubscriptionId $subscriptionId
Register-AzureRmResourceProvider -ProviderNamespace 'Microsoft.KeyVault'

#Creates the a new resource group and Key-Vault
New-AzureRmResourceGroup -Name $ResourceGroup -Location $Location
New-AzureRmKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroup -Location $Location -sku standard -EnabledForDeployment 

#Converts the plain text password into a secure string
$SecurePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force

#Creates a new selfsigned cert and exports a pfx cert to a directory on disk
$NewCert = New-SelfSignedCertificate -CertStoreLocation Cert:\CurrentUser\My -DnsName $CertDNSName 
Export-PfxCertificate -FilePath $CertFileFullPath -Password $SecurePassword -Cert $NewCert
Import-PfxCertificate -FilePath $CertFileFullPath -Password $SecurePassword -CertStoreLocation Cert:\LocalMachine\My 

#Reads the content of the certificate and converts it into a json format
$Bytes = [System.IO.File]::ReadAllBytes($CertFileFullPath)
$Base64 = [System.Convert]::ToBase64String($Bytes)

$JSONBlob = @{
    data = $Base64
    dataType = 'pfx'
    password = $Password
} | ConvertTo-Json

$ContentBytes = [System.Text.Encoding]::UTF8.GetBytes($JSONBlob)
$Content = [System.Convert]::ToBase64String($ContentBytes)

#Converts the json content a secure string
$SecretValue = ConvertTo-SecureString -String $Content -AsPlainText -Force

#Creates a new secret in Azure Key Vault
$NewSecret = Set-AzureKeyVaultSecret -VaultName $KeyVaultName -Name $KeyVaultSecretName -SecretValue $SecretValue -Verbose

#Writes out the information you need for creating a secure cluster
Write-Host
Write-Host "Resource Id: "$(Get-AzureRmKeyVault -VaultName $KeyVaultName).ResourceId
Write-Host "Secret URL : "$NewSecret.Id
Write-Host "Thumbprint : "$NewCert.Thumbprint 


