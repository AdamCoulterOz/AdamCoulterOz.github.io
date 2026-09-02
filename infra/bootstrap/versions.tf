terraform {
  required_version = ">= 1.15.0, < 2.0.0"

  required_providers {
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.9"
    }

    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.53"
    }

    time = {
      source  = "hashicorp/time"
      version = "~> 0.14.0"
    }
  }
}
