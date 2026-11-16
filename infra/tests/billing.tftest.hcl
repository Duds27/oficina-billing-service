# Testes sem conta AWS: provider mockado e o estado do infra-k8s simulado.
# Rodar com: terraform -chdir=infra init -backend=false && terraform -chdir=infra test

mock_provider "aws" {}

override_data {
  target = data.terraform_remote_state.k8s
  values = {
    outputs = {
      vpc_id                  = "vpc-123"
      private_subnet_ids      = ["subnet-a", "subnet-b"]
      nodes_security_group_id = "sg-nos-eks"
    }
  }
}

variables {
  environment  = "homolog"
  state_bucket = "bucket-de-teste"
}

run "rds_postgres_16_privado_e_criptografado" {
  command = plan

  assert {
    condition     = aws_db_instance.billing.engine == "postgres" && aws_db_instance.billing.engine_version == "16"
    error_message = "O banco do billing é PostgreSQL 16."
  }

  assert {
    condition     = aws_db_instance.billing.instance_class == "db.t4g.micro"
    error_message = "Classe padrão db.t4g.micro."
  }

  assert {
    condition     = aws_db_instance.billing.publicly_accessible == false && aws_db_instance.billing.storage_encrypted
    error_message = "O RDS não pode ser público e precisa estar criptografado."
  }

  assert {
    condition     = aws_db_subnet_group.billing.subnet_ids == toset(["subnet-a", "subnet-b"])
    error_message = "O RDS fica nas subnets privadas do infra-k8s."
  }

  assert {
    condition     = aws_db_instance.billing.storage_type == "gp2" && aws_db_instance.billing.backup_retention_period == 0
    error_message = "No free tier: gp2 e sem retenção de backup."
  }

  assert {
    condition     = aws_db_instance.billing.skip_final_snapshot && aws_db_instance.billing.deletion_protection == false
    error_message = "Em homolog o banco pode ser destruído sem snapshot."
  }
}

run "so_os_nos_do_eks_acessam_o_banco" {
  command = plan

  assert {
    condition = alltrue([
      for r in aws_security_group.billing_db.ingress :
      r.from_port == 5432 && r.to_port == 5432 && r.security_groups == toset(["sg-nos-eks"]) && try(length(r.cidr_blocks), 0) == 0
    ])
    error_message = "A única entrada é a 5432, vinda do security group dos nós do EKS."
  }

  assert {
    condition     = length(aws_security_group.billing_db.ingress) == 1
    error_message = "Nenhuma outra regra de entrada."
  }
}

run "secrets_com_os_nomes_que_o_cd_le" {
  command = plan

  assert {
    condition     = aws_secretsmanager_secret.billing_db.name == "oficina-homolog/billing/db"
    error_message = "Secret do banco fora do padrão."
  }

  assert {
    condition     = aws_secretsmanager_secret.mercadopago.name == "oficina-homolog/billing/mercadopago"
    error_message = "Secret do Mercado Pago fora do padrão."
  }

  assert {
    condition     = aws_secretsmanager_secret.billing_db.recovery_window_in_days == 0
    error_message = "Em homolog o secret é apagado na hora (permite recriar)."
  }
}

run "prod_protege_banco_secrets_e_ecr" {
  command = plan

  variables {
    environment = "prod"
    free_tier   = false
  }

  assert {
    condition     = aws_db_instance.billing.deletion_protection && aws_db_instance.billing.skip_final_snapshot == false
    error_message = "Em prod: proteção contra exclusão e snapshot final."
  }

  assert {
    condition     = aws_db_instance.billing.storage_type == "gp3" && aws_db_instance.billing.backup_retention_period == 7
    error_message = "Fora do free tier: gp3 e 7 dias de backup."
  }

  assert {
    condition     = aws_secretsmanager_secret.mercadopago.recovery_window_in_days == 30 && aws_ecr_repository.billing_service.force_delete == false
    error_message = "Em prod, os secrets têm janela de recuperação e o ECR não é apagado com imagens."
  }
}

run "ambiente_invalido_e_recusado" {
  command = plan

  variables {
    environment = "dev"
  }

  expect_failures = [var.environment]
}
