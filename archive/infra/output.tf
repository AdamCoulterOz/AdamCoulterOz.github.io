output "AZURE_LOCATION" { value = data.azurerm_resource_group.project.location }
output "AZURE_RESOURCE_GROUP" { value = data.azurerm_resource_group.project.name }
output "AZURE_FUNCTION_NAME" { value = azurerm_function_app_flex_consumption.archive.name }
output "SERVICE_API_NAME" { value = azurerm_function_app_flex_consumption.archive.name }
output "SERVICE_API_IDENTITY_PRINCIPAL_ID" { value = azurerm_user_assigned_identity.archive.principal_id }
output "SITE_APPLICATIONINSIGHTS_NAME" { value = azurerm_application_insights.site.name }
output "FUNCTION_APPLICATIONINSIGHTS_NAME" { value = azurerm_application_insights.function.name }
output "ARCHIVE_STORAGE_ACCOUNT_NAME" { value = azurerm_storage_account.archive.name }
