terraform {
  required_version = ">= 1.15.0, < 2.0.0"
  backend "azurerm" {}
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "5.3.0"
    }
    time = {
      source  = "hashicorp/time"
      version = "0.14.1"
    }
  }
}
provider "azurerm" {
  resource_provider_registrations = "none"
  storage_use_azuread             = true
  features {}
}
