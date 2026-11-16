output "db_endpoint" {
  description = "Endereço do RDS PostgreSQL do billing."
  value       = aws_db_instance.billing.address
}

output "db_secret_name" {
  description = "Secret com as credenciais do banco (variável DB_SECRET_ID do CD)."
  value       = aws_secretsmanager_secret.billing_db.name
}

output "mercadopago_secret_name" {
  description = "Secret do Mercado Pago, a preencher manualmente (variável MP_SECRET_ID do CD)."
  value       = aws_secretsmanager_secret.mercadopago.name
}

output "db_security_group_id" {
  value = aws_security_group.billing_db.id
}

output "ecr_repository_url" {
  description = "Repositório da imagem do serviço."
  value       = aws_ecr_repository.billing_service.repository_url
}
