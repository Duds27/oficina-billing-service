# Evidências de cobertura

Relatório do próprio CI da `main` (job `build-test-quality`), consolidado pelo ReportGenerator a partir de
todos os projetos de teste. O gate do CI exige 80% de linhas, e o SonarCloud tem o quality gate dele.

| | |
| --- | --- |
| Cobertura de linhas | **94.7%** (1073 de 1133 linhas) |
| Cobertura de ramos | 87.6% |
| Commit | [`5ed3524`](https://github.com/Duds27/oficina-billing-service/commit/5ed3524) |
| Run do CI | [44061760429](https://github.com/Duds27/oficina-billing-service/actions/runs/44061760429) (artifact `coverage`) |
| SonarCloud | [quality gate e cobertura](https://sonarcloud.io/summary/new_code?id=Duds27_oficina-billing-service) |

![Cobertura de linhas](badge_linecoverage.svg) ![Cobertura de ramos](badge_branchcoverage.svg)

O detalhe por assembly e classe está em [`cobertura-resumo.md`](cobertura-resumo.md). O relatório
completo, mais novo, sai em todo run do CI, no artifact `coverage`.
