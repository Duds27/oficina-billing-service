variable "aws_region" {
  type        = string
  default     = "us-east-1"
  description = "Região AWS."
}

variable "project" {
  type        = string
  default     = "oficina"
  description = "Prefixo de nomeação dos recursos."
}

variable "environment" {
  type        = string
  description = "Ambiente (homolog | prod)."

  validation {
    condition     = contains(["homolog", "prod"], var.environment)
    error_message = "environment deve ser homolog ou prod."
  }
}

variable "state_bucket" {
  type        = string
  description = "Bucket S3 com os estados remotos (o do infra-k8s é lido daqui)."
}

variable "instance_class" {
  type        = string
  default     = "db.t4g.micro"
  description = "Classe do RDS PostgreSQL."
}

variable "allocated_storage" {
  type        = number
  default     = 20
  description = "Disco do RDS, em GB."
}

variable "free_tier" {
  type        = bool
  default     = true
  description = "Mesmo ajuste do infra-database: single-AZ, sem retenção de backup e storage gp2 (limites do AWS Free Plan). Em conta paga, false usa gp3 e 7 dias de backup."
}

variable "db_name" {
  type    = string
  default = "billing"
}

variable "db_username" {
  type    = string
  default = "billing"
}
