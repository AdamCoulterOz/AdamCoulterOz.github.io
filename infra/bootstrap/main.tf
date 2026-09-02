data "azuread_client_config" "current" {}

data "azurerm_client_config" "current" {}

locals {
  github_oidc_issuer   = "https://token.actions.githubusercontent.com"
  github_oidc_audience = "api://AzureADTokenExchange"

  owner_role_definition_id              = "8e3af657-a8ff-443c-a75c-2fe8c4bcb635"
  storage_blob_data_contributor_role_id = "ba92f5b4-2d11-453d-a403-e96b0029c9fe"

  tags = {
    managed-by = "terraform"
    component  = "bootstrap"
    repository = "AdamCoulterOz/AdamCoulterOz.github.io"
  }
}

check "signed_in_context_matches_approved_scope" {
  assert {
    condition     = data.azurerm_client_config.current.subscription_id == var.subscription_id
    error_message = "The authenticated Azure context does not match the approved subscription."
  }

  assert {
    condition     = data.azurerm_client_config.current.tenant_id == var.tenant_id && data.azuread_client_config.current.tenant_id == var.tenant_id
    error_message = "The authenticated AzureRM or AzureAD context does not match the approved tenant."
  }
}

resource "azurerm_resource_group" "project" {
  name     = var.resource_group_name
  location = var.location
  tags     = local.tags
}

resource "azurerm_storage_account" "terraform_state" {
  name                            = var.state_storage_account_name
  resource_group_name             = azurerm_resource_group.project.name
  location                        = azurerm_resource_group.project.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  account_kind                    = "StorageV2"
  access_tier                     = "Hot"
  https_traffic_only_enabled      = true
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  local_user_enabled              = false
  default_to_oauth_authentication = true
  public_network_access_enabled   = true
  tags                            = local.tags

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 30
    }

    container_delete_retention_policy {
      days = 30
    }
  }
}

resource "azuread_application" "github_actions" {
  display_name            = var.github_actions_app_display_name
  sign_in_audience        = "AzureADMyOrg"
  owners                  = [data.azuread_client_config.current.object_id]
  prevent_duplicate_names = true
}

resource "azuread_service_principal" "github_actions" {
  client_id = azuread_application.github_actions.client_id
}

resource "azuread_application_federated_identity_credential" "github_pages_environment" {
  application_id = azuread_application.github_actions.id
  display_name   = "github-pages-environment"
  description    = "GitHub Actions for AdamCoulterOz/AdamCoulterOz.github.io in the github-pages environment."
  audiences      = [local.github_oidc_audience]
  issuer         = local.github_oidc_issuer
  subject        = var.github_oidc_subject
}

# GitHub Actions can manage only the project resource group, never the subscription.
resource "azurerm_role_assignment" "github_actions_project_owner" {
  scope                            = azurerm_resource_group.project.id
  role_definition_id               = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/providers/Microsoft.Authorization/roleDefinitions/${local.owner_role_definition_id}"
  principal_id                     = azuread_service_principal.github_actions.object_id
  principal_type                   = "ServicePrincipal"
  skip_service_principal_aad_check = true
}

# State access is data-plane RBAC; owning the resource group alone is insufficient.
resource "azurerm_role_assignment" "github_actions_state_blob_data_contributor" {
  scope                            = azurerm_storage_account.terraform_state.id
  role_definition_id               = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/providers/Microsoft.Authorization/roleDefinitions/${local.storage_blob_data_contributor_role_id}"
  principal_id                     = azuread_service_principal.github_actions.object_id
  principal_type                   = "ServicePrincipal"
  skip_service_principal_aad_check = true
}

# The signed-in bootstrap operator needs data-plane access to safely migrate and
# subsequently inspect this state using Entra authentication, never account keys.
resource "azurerm_role_assignment" "bootstrap_operator_state_blob_data_contributor" {
  scope              = azurerm_storage_account.terraform_state.id
  role_definition_id = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/providers/Microsoft.Authorization/roleDefinitions/${local.storage_blob_data_contributor_role_id}"
  principal_id       = data.azurerm_client_config.current.object_id
}

# Storage Container creation uses Entra data-plane authorization because shared
# keys are disabled. Give Azure RBAC a bounded interval to propagate the new
# operator assignment before attempting that first data-plane operation.
resource "time_sleep" "operator_state_blob_data_rbac_propagation" {
  create_duration = "10m"

  depends_on = [azurerm_role_assignment.bootstrap_operator_state_blob_data_contributor]
}

resource "azurerm_storage_container" "terraform_state" {
  name                  = var.state_container_name
  storage_account_id    = azurerm_storage_account.terraform_state.id
  container_access_type = "private"

  depends_on = [time_sleep.operator_state_blob_data_rbac_propagation]
}
