output "github_actions_client_id" {
  description = "Application (client) ID to set as the non-secret AZURE_CLIENT_ID GitHub environment variable."
  value       = azuread_application.github_actions.client_id
}

output "github_actions_service_principal_object_id" {
  description = "Object ID of the service principal receiving project and Terraform-state RBAC."
  value       = azuread_service_principal.github_actions.object_id
}

output "resource_group_name" {
  description = "Project resource group for subsequent Terraform roots."
  value       = azurerm_resource_group.project.name
}

output "state_storage_account_name" {
  description = "Terraform state storage account name."
  value       = azurerm_storage_account.terraform_state.name
}

output "state_container_name" {
  description = "Private Terraform state container name."
  value       = azurerm_storage_container.terraform_state.name
}

output "bootstrap_backend_key" {
  description = "Remote backend key reserved for this bootstrap root."
  value       = "bootstrap/terraform.tfstate"
}

output "tenant_id" {
  description = "Tenant ID to set as the non-secret AZURE_TENANT_ID GitHub environment variable."
  value       = var.tenant_id
}

output "subscription_id" {
  description = "Subscription ID to set as the non-secret AZURE_SUBSCRIPTION_ID GitHub environment variable."
  value       = var.subscription_id
}
