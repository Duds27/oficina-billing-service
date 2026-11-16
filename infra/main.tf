locals {
  name = "${var.project}-${var.environment}"

  tags = {
    Project     = var.project
    Environment = var.environment
    Service     = "billing-service"
    ManagedBy   = "terraform"
  }
}

# Rede e nós do EKS, do oficina-infra-k8s.
data "terraform_remote_state" "k8s" {
  backend = "s3"
  config = {
    bucket = var.state_bucket
    key    = "infra-k8s/${var.environment}/terraform.tfstate"
    region = var.aws_region
  }
}

# ───────────────────── RDS PostgreSQL 16 (só do billing) ─────────────────────
resource "random_password" "db" {
  length  = 32
  special = false # evita caracteres problemáticos na connection string
}

resource "aws_db_subnet_group" "billing" {
  name       = "${local.name}-billing-db-subnets"
  subnet_ids = data.terraform_remote_state.k8s.outputs.private_subnet_ids
}

# Só os nós do EKS falam com o banco do billing: nenhum outro serviço tem rota até ele.
resource "aws_security_group" "billing_db" {
  name        = "${local.name}-billing-db-sg"
  description = "RDS PostgreSQL do billing-service"
  vpc_id      = data.terraform_remote_state.k8s.outputs.vpc_id

  ingress {
    description     = "PostgreSQL a partir dos nos do EKS"
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [data.terraform_remote_state.k8s.outputs.nodes_security_group_id]
  }
}

resource "aws_db_instance" "billing" {
  identifier     = "${local.name}-billing"
  engine         = "postgres"
  engine_version = "16"
  instance_class = var.instance_class

  db_name  = var.db_name
  username = var.db_username
  password = random_password.db.result
  port     = 5432

  allocated_storage    = var.allocated_storage
  storage_type         = var.free_tier ? "gp2" : "gp3"
  storage_encrypted    = true
  multi_az             = false
  db_subnet_group_name = aws_db_subnet_group.billing.name

  vpc_security_group_ids = [aws_security_group.billing_db.id]
  publicly_accessible    = false

  auto_minor_version_upgrade = true
  backup_retention_period    = var.free_tier ? 0 : 7
  deletion_protection        = var.environment == "prod"
  skip_final_snapshot        = var.environment != "prod"
  final_snapshot_identifier  = var.environment == "prod" ? "${local.name}-billing-final" : null
  apply_immediately          = true
}

# ───────────────────────────── Secrets ─────────────────────────────
# Mesmo formato que o CD já lê (host, port, username, password, dbname).
resource "aws_secretsmanager_secret" "billing_db" {
  name                    = "${local.name}/billing/db"
  recovery_window_in_days = var.environment == "prod" ? 30 : 0
}

resource "aws_secretsmanager_secret_version" "billing_db" {
  secret_id = aws_secretsmanager_secret.billing_db.id
  secret_string = jsonencode({
    host     = aws_db_instance.billing.address
    port     = aws_db_instance.billing.port
    username = var.db_username
    password = random_password.db.result
    dbname   = var.db_name
  })
}

# Criado vazio: o token de TESTE e o segredo do webhook são preenchidos à mão, pelo console ou
# pela CLI, e nunca passam pelo Terraform (nem pelo state). Formato esperado:
#   {"accessToken": "TEST-...", "webhookSecret": "..."}
resource "aws_secretsmanager_secret" "mercadopago" {
  name                    = "${local.name}/billing/mercadopago"
  description             = "Mercado Pago (sandbox): accessToken e webhookSecret. Preenchido manualmente."
  recovery_window_in_days = var.environment == "prod" ? 30 : 0
}

# ───────────────────────────── ECR ─────────────────────────────
resource "aws_ecr_repository" "billing_service" {
  name                 = "${local.name}-billing-service"
  image_tag_mutability = "MUTABLE"
  force_delete         = var.environment != "prod"

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecr_lifecycle_policy" "billing_service" {
  repository = aws_ecr_repository.billing_service.name
  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Mantém as 20 imagens mais recentes"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 20
      }
      action = { type = "expire" }
    }]
  })
}
