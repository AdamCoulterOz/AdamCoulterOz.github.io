variable "subscription_id" {
  description = "Subscription that owns the project resource group. This bootstrap root is deliberately single-subscription."
  type        = string
  default     = "a26059bf-5574-47e9-b3e4-6a46a19d2407"

  validation {
    condition     = var.subscription_id == "a26059bf-5574-47e9-b3e4-6a46a19d2407"
    error_message = "This bootstrap is approved only for subscription a26059bf-5574-47e9-b3e4-6a46a19d2407."
  }
}

variable "tenant_id" {
  description = "Tenant containing the GitHub Actions application registration."
  type        = string
  default     = "a098ad4f-34e6-46c8-aa14-e09f46c86f2e"

  validation {
    condition     = var.tenant_id == "a098ad4f-34e6-46c8-aa14-e09f46c86f2e"
    error_message = "This bootstrap is approved only for tenant a098ad4f-34e6-46c8-aa14-e09f46c86f2e."
  }
}

variable "location" {
  description = "Azure location for project and state resources."
  type        = string
  default     = "Australia East"

  validation {
    condition     = var.location == "Australia East"
    error_message = "This bootstrap is approved only for Australia East."
  }
}

variable "resource_group_name" {
  description = "Dedicated resource group for the GitHub Pages telemetry workload."
  type        = string
  default     = "rg-adamcoulter-github-pages-aue"

  validation {
    condition     = var.resource_group_name == "rg-adamcoulter-github-pages-aue"
    error_message = "This bootstrap must use rg-adamcoulter-github-pages-aue."
  }
}

variable "state_storage_account_name" {
  description = "Globally unique StorageV2 account for Terraform state."
  type        = string
  default     = "stadamcgpiac1319345545"

  validation {
    condition     = var.state_storage_account_name == "stadamcgpiac1319345545"
    error_message = "This bootstrap must use stadamcgpiac1319345545 for state."
  }
}

variable "state_container_name" {
  description = "Private blob container holding Terraform state."
  type        = string
  default     = "tfstate"

  validation {
    condition     = var.state_container_name == "tfstate"
    error_message = "This bootstrap must use the tfstate container."
  }
}

variable "github_actions_app_display_name" {
  description = "Display name of the single-tenant Entra application used by GitHub Actions."
  type        = string
  default     = "AdamCoulterOz.github.io GitHub Actions"

  validation {
    condition     = var.github_actions_app_display_name == "AdamCoulterOz.github.io GitHub Actions"
    error_message = "Use the approved GitHub Actions application display name."
  }
}

variable "github_oidc_subject" {
  description = "Exact GitHub OIDC subject allowed to exchange a token."
  type        = string
  default     = "repo:AdamCoulterOz/AdamCoulterOz.github.io:environment:github-pages"

  validation {
    condition     = var.github_oidc_subject == "repo:AdamCoulterOz/AdamCoulterOz.github.io:environment:github-pages"
    error_message = "The GitHub OIDC subject must remain environment-scoped to github-pages."
  }
}
