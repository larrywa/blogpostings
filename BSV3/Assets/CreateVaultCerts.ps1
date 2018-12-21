# ************************************************************************************
# The purpose of this script is to:
#	1. Create the certificate to be used for the SF cluster
#   2. Create a new Azure Key Vault resource group and vault to store the cert in
#	3. Export the cert to your hard drive
#	4. Import the cert to your local machineremove
#	5. Generate output to be used when setting up your cluster ARM template
# ************************************************************************************

#Name of the Key Vault service
# No capital letters!
$KeyVaultName = "" 
#Resource group for the Key-Vault service. 
$ResourceGroup = ""
#Set the Subscription
$subscriptionId = "" 
#Azure data center locations (East US", "West US" etc)
$Location = ""
# DO NOT use $$ in your password
#Password for the certificate
$Password = ""

#DNS name for the certificate
$CertDNSName = ""
#Name of the secret in key vault
$KeyVaultSecretName = ""
#Path to directory on local disk in which the certificate is stored  
$CertFileFullPath = "C:\Certs\$CertDNSName.pfx"

#If more than one under your account
Select-AzureRmSubscription -SubscriptionId $subscriptionId
#Verify Current Subscription
Get-AzureRmSubscription –SubscriptionId $subscriptionId
Register-AzureRmResourceProvider -ProviderNamespace 'Microsoft.KeyVault'

#Creates the a new resource group and Key-Vault
New-AzureRmResourceGroup -Name $ResourceGroup -Location $Location

Write-Host "Creating key vault..."
Write-Host
New-AzureRmKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroup -Location $Location -sku standard -WarningAction SilentlyContinue -EnabledForDeployment

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
Write-Host "Setting secret in key vault..."
Write-Host
$NewSecret = Set-AzureKeyVaultSecret -VaultName $KeyVaultName -Name $KeyVaultSecretName -SecretValue $SecretValue -Verbose

#Writes out the information you need for creating a secure cluster
Write-Host
Write-Host "Save this information to your text editor..."
Write-Host "Resource Id: "$(Get-AzureRmKeyVault -VaultName $KeyVaultName).ResourceId
Write-Host "Secret URL : "$NewSecret.Id
Write-Host "Thumbprint : "$NewCert.Thumbprint 


