FROM mcr.microsoft.com/dotnet/sdk:8.0.424-bookworm-slim@sha256:bb32ba3ba3ea36e38572d9d8db76fa15f7cbf722f3f886e06bca6d528bd4fba8
WORKDIR /issuer
COPY infrastructure/load/Issuer/ .
RUN dotnet publish -c Release -o /app
ENTRYPOINT ["dotnet", "/app/Issuer.dll"]
