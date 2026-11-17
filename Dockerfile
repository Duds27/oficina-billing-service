# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Arquivos de projeto primeiro, para aproveitar o cache do restore
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/Oficina.Billing.Domain/Oficina.Billing.Domain.csproj src/Oficina.Billing.Domain/
COPY src/Oficina.Billing.Application/Oficina.Billing.Application.csproj src/Oficina.Billing.Application/
COPY src/Oficina.Billing.Infrastructure/Oficina.Billing.Infrastructure.csproj src/Oficina.Billing.Infrastructure/
COPY src/Oficina.Billing.API/Oficina.Billing.API.csproj src/Oficina.Billing.API/
RUN dotnet restore src/Oficina.Billing.API/Oficina.Billing.API.csproj

COPY src/ src/
RUN dotnet publish src/Oficina.Billing.API/Oficina.Billing.API.csproj -c Release -o /publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /publish .

# Usuário não-root 'app' (UID 1654), já presente nas imagens .NET 8.
USER $APP_UID

# Observabilidade: o OpenTelemetry da aplicação manda traces, métricas e logs por OTLP
# (src/Oficina.Billing.API/Observabilidade). SERVICE_VERSION vira o service.version; o CD passa o SHA.
ARG SERVICE_VERSION=dev
ENV SERVICE_VERSION=${SERVICE_VERSION}

EXPOSE 8080
ENTRYPOINT ["dotnet", "Oficina.Billing.API.dll"]
