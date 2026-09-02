# Adapted from Azure-Samples/functions-quickstart-dotnet-azd-terraform at
# 38f0b0a09626cca7bce678ab9b4d5092f7d9c219. Bootstrap owns the resource group and OIDC trust.
locals {
  tags                         = { "azd-env-name" = var.environment_name, "workload" = "github-pages-telemetry-archive" }
  function_app_name            = "func-adamcgparch-${var.repository_id}"
  function_storage_name        = "stadamcgpfunc${var.repository_id}"
  deployment_storage_container = "app-package-${var.repository_id}"
}

data "azurerm_resource_group" "project" { name = var.resource_group_name }
data "azurerm_client_config" "current" {}

resource "azurerm_user_assigned_identity" "archive" {
  name                = "id-adamcgp-archive-${var.repository_id}"
  location            = data.azurerm_resource_group.project.location
  resource_group_name = data.azurerm_resource_group.project.name
  tags                = local.tags
}

resource "azurerm_log_analytics_workspace" "workspace" {
  name                = "log-adamcgp-${var.repository_id}"
  location            = data.azurerm_resource_group.project.location
  resource_group_name = data.azurerm_resource_group.project.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  # Runtime queries authenticate with the Function's UAMI and RBAC only.
  local_authentication_enabled = false
  tags                         = local.tags
}

# Browser ingestion is public configuration by design; the worker component is Entra-only.
resource "azurerm_application_insights" "site" {
  name                                 = "appi-adamcgp-site-${var.repository_id}"
  location                             = data.azurerm_resource_group.project.location
  resource_group_name                  = data.azurerm_resource_group.project.name
  workspace_id                         = azurerm_log_analytics_workspace.workspace.id
  application_type                     = "web"
  local_authentication_enabled         = true
  daily_data_cap_in_gb                 = 0.1
  daily_data_cap_notifications_enabled = true
  tags                                 = local.tags
}

resource "azurerm_application_insights" "function" {
  name                                 = "appi-adamcgp-archive-${var.repository_id}"
  location                             = data.azurerm_resource_group.project.location
  resource_group_name                  = data.azurerm_resource_group.project.name
  workspace_id                         = azurerm_log_analytics_workspace.workspace.id
  application_type                     = "web"
  local_authentication_enabled         = false
  daily_data_cap_in_gb                 = 0.1
  daily_data_cap_notifications_enabled = true
  tags                                 = local.tags
}

resource "azurerm_storage_account" "function" {
  name                            = local.function_storage_name
  resource_group_name             = data.azurerm_resource_group.project.name
  location                        = data.azurerm_resource_group.project.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  account_kind                    = "StorageV2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  local_user_enabled              = false
  default_to_oauth_authentication = true
  min_tls_version                 = "TLS1_2"
  tags                            = local.tags
}

resource "azurerm_storage_account" "archive" {
  name                            = "stadamcgparch${var.repository_id}"
  resource_group_name             = data.azurerm_resource_group.project.name
  location                        = data.azurerm_resource_group.project.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  account_kind                    = "StorageV2"
  access_tier                     = "Hot"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  local_user_enabled              = false
  default_to_oauth_authentication = true
  min_tls_version                 = "TLS1_2"
  tags                            = local.tags
  blob_properties {
    versioning_enabled = true
    delete_retention_policy { days = 30 }
    container_delete_retention_policy { days = 30 }
  }
}

resource "azurerm_storage_management_policy" "archive_tiering" {
  storage_account_id = azurerm_storage_account.archive.id
  rule {
    name    = "tier-raw-records-without-deleting"
    enabled = true
    filters {
      blob_types   = ["blockBlob"]
      prefix_match = ["raw/"]
    }
    actions {
      base_blob {
        tier_to_cool_after_days_since_modification_greater_than    = 30
        tier_to_archive_after_days_since_modification_greater_than = 180
      }
    }
  }
}

# Terraform's authenticated deployer needs data-plane access to create private containers.
resource "azurerm_role_assignment" "function_storage_deployer" {
  scope                = azurerm_storage_account.function.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}
resource "azurerm_role_assignment" "archive_storage_deployer" {
  scope                = azurerm_storage_account.archive.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}
resource "azurerm_storage_container" "deployment_package" {
  name                  = local.deployment_storage_container
  storage_account_id    = azurerm_storage_account.function.id
  container_access_type = "private"
  depends_on            = [time_sleep.storage_deployer_rbac_propagation]
}
resource "azurerm_storage_container" "raw" {
  name                  = "raw"
  storage_account_id    = azurerm_storage_account.archive.id
  container_access_type = "private"
  depends_on            = [time_sleep.storage_deployer_rbac_propagation]
}
resource "azurerm_storage_container" "control" {
  name                  = "control"
  storage_account_id    = azurerm_storage_account.archive.id
  container_access_type = "private"
  depends_on            = [time_sleep.storage_deployer_rbac_propagation]
}

resource "azurerm_service_plan" "flex" {
  name                = "plan-adamcgp-archive-${var.repository_id}"
  location            = data.azurerm_resource_group.project.location
  resource_group_name = data.azurerm_resource_group.project.name
  os_type             = "Linux"
  sku_name            = "FC1"
  tags                = local.tags
}

resource "azurerm_role_assignment" "function_host_blob" {
  scope                = azurerm_storage_account.function.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = azurerm_user_assigned_identity.archive.principal_id
  principal_type       = "ServicePrincipal"
  depends_on           = [time_sleep.identity_principal_propagation]
}
resource "azurerm_role_assignment" "function_host_queue" {
  scope                = azurerm_storage_account.function.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = azurerm_user_assigned_identity.archive.principal_id
  principal_type       = "ServicePrincipal"
  depends_on           = [time_sleep.identity_principal_propagation]
}
resource "azurerm_role_assignment" "function_host_table" {
  scope                = azurerm_storage_account.function.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.archive.principal_id
  principal_type       = "ServicePrincipal"
  depends_on           = [time_sleep.identity_principal_propagation]
}
resource "azurerm_role_assignment" "archive_writer" {
  scope                = azurerm_storage_account.archive.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.archive.principal_id
  principal_type       = "ServicePrincipal"
  depends_on           = [time_sleep.identity_principal_propagation]
}
resource "azurerm_role_assignment" "log_reader" {
  scope                = azurerm_log_analytics_workspace.workspace.id
  role_definition_name = "Log Analytics Data Reader"
  principal_id         = azurerm_user_assigned_identity.archive.principal_id
  principal_type       = "ServicePrincipal"
  depends_on           = [time_sleep.identity_principal_propagation]
}
resource "azurerm_role_assignment" "function_metrics" {
  scope                = azurerm_application_insights.function.id
  role_definition_name = "Monitoring Metrics Publisher"
  principal_id         = azurerm_user_assigned_identity.archive.principal_id
  principal_type       = "ServicePrincipal"
  depends_on           = [time_sleep.identity_principal_propagation]
}

# Azure documents a short identity-principal propagation delay and up to ten
# minutes for RBAC changes. Bound these first-create gates instead of allowing
# a storage container or Function host to race the data-plane permissions.
resource "time_sleep" "identity_principal_propagation" {
  create_duration = "30s"
  depends_on      = [azurerm_user_assigned_identity.archive]
}

resource "time_sleep" "storage_deployer_rbac_propagation" {
  create_duration = "10m"
  depends_on = [
    azurerm_role_assignment.function_storage_deployer,
    azurerm_role_assignment.archive_storage_deployer
  ]
}

resource "time_sleep" "function_identity_rbac_propagation" {
  create_duration = "10m"
  depends_on = [
    azurerm_role_assignment.function_host_blob,
    azurerm_role_assignment.function_host_queue,
    azurerm_role_assignment.function_host_table,
    azurerm_role_assignment.archive_writer,
    azurerm_role_assignment.log_reader,
    azurerm_role_assignment.function_metrics
  ]
}

resource "azurerm_function_app_flex_consumption" "archive" {
  name                                           = local.function_app_name
  location                                       = data.azurerm_resource_group.project.location
  resource_group_name                            = data.azurerm_resource_group.project.name
  service_plan_id                                = azurerm_service_plan.flex.id
  storage_container_type                         = "blobContainer"
  storage_container_endpoint                     = "${azurerm_storage_account.function.primary_blob_endpoint}${azurerm_storage_container.deployment_package.name}"
  storage_authentication_type                    = "UserAssignedIdentity"
  storage_user_assigned_identity_id              = azurerm_user_assigned_identity.archive.id
  runtime_name                                   = "dotnet-isolated"
  runtime_version                                = "10.0"
  maximum_instance_count                         = 1
  instance_memory_in_mb                          = 2048
  https_only                                     = true
  webdeploy_publish_basic_authentication_enabled = false
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.archive.id]
  }
  site_config {
    application_insights_connection_string = azurerm_application_insights.function.connection_string
    # Omitting always_ready keeps Flex at zero always-ready instances.
  }
  app_settings = {
    "FUNCTIONS_EXTENSION_VERSION"                = "~4"
    "AzureWebJobsStorage__credential"            = "managedidentity"
    "AzureWebJobsStorage__clientId"              = azurerm_user_assigned_identity.archive.client_id
    "AzureWebJobsStorage__blobServiceUri"        = azurerm_storage_account.function.primary_blob_endpoint
    "AzureWebJobsStorage__queueServiceUri"       = azurerm_storage_account.function.primary_queue_endpoint
    "AzureWebJobsStorage__tableServiceUri"       = azurerm_storage_account.function.primary_table_endpoint
    "APPLICATIONINSIGHTS_CONNECTION_STRING"      = azurerm_application_insights.function.connection_string
    "APPLICATIONINSIGHTS_AUTHENTICATION_STRING"  = "ClientId=${azurerm_user_assigned_identity.archive.client_id};Authorization=AAD"
    "ARCHIVE__WorkspaceId"                       = azurerm_log_analytics_workspace.workspace.workspace_id
    "ARCHIVE__SiteApplicationInsightsResourceId" = azurerm_application_insights.site.id
    "ARCHIVE__ArchiveBlobServiceUri"             = azurerm_storage_account.archive.primary_blob_endpoint
    "ARCHIVE__ArchiveStorageAccountName"         = azurerm_storage_account.archive.name
    "ARCHIVE__ManagedIdentityClientId"           = azurerm_user_assigned_identity.archive.client_id
  }
  tags = merge(local.tags, { "azd-service-name" = "archive" })
  depends_on = [
    time_sleep.function_identity_rbac_propagation,
    azurerm_storage_container.deployment_package
  ]
}

# AzureRM 5.3 does not model the site's FTP basic-publishing policy. Azure
# materializes this `ftp` child with the site, so it must be patched rather
# than created or adopted as an independently owned ARM resource. AzAPI's
# update resource preserves that ownership boundary while retaining the
# Microsoft.Web 2025-03-01 `properties.allow` control declaratively.
resource "azapi_update_resource" "archive_ftp_basic_publishing" {
  type        = "Microsoft.Web/sites/basicPublishingCredentialsPolicies@2025-03-01"
  resource_id = "${azurerm_function_app_flex_consumption.archive.id}/basicPublishingCredentialsPolicies/ftp"
  body = {
    properties = {
      allow = false
    }
  }
  response_export_values = ["properties.allow"]
}

resource "azurerm_monitor_action_group" "archive_failure" {
  name                = "ag-adamcgp-archive-failure"
  resource_group_name = data.azurerm_resource_group.project.name
  short_name          = "archfail"
  email_receiver {
    name                    = "archive-owner"
    email_address           = "ads@me.com"
    use_common_alert_schema = true
  }
  tags = local.tags
}

# The current scheduled-query API only permits a maximum 48-hour lookback. This
# is therefore a real failure alert, not a misleading substitute for 8-day success coverage.
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "archive_failure" {
  name                 = "sqr-adamcgp-archive-failure"
  resource_group_name  = data.azurerm_resource_group.project.name
  location             = data.azurerm_resource_group.project.location
  scopes               = [azurerm_log_analytics_workspace.workspace.id]
  severity             = 2
  evaluation_frequency = "PT1H"
  window_duration      = "P1D"
  description          = "The weekly telemetry archive function logged a failed run."
  enabled              = true
  criteria {
    query                   = <<-KQL
      AppTraces
      | where _ResourceId =~ '${azurerm_application_insights.function.id}'
      | where Message == 'Archive run failed.'
    KQL
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }
  action {
    action_groups = [azurerm_monitor_action_group.archive_failure.id]
    email_subject = "GitHub Pages telemetry archive failed"
  }
  tags = local.tags
}
