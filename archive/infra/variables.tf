variable "location" {
  type    = string
  default = "australiaeast"
}

variable "environment_name" {
  type = string
}

variable "resource_group_name" {
  description = "Existing bootstrap-created resource group; this stack must never create it."
  type        = string
}

variable "repository_id" {
  type    = string
  default = "1319345545"
}
