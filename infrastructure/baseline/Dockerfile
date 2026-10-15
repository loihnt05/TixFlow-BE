FROM mcr.microsoft.com/dotnet/sdk:8.0.424-bookworm-slim@sha256:bb32ba3ba3ea36e38572d9d8db76fa15f7cbf722f3f886e06bca6d528bd4fba8
WORKDIR /src
COPY . .
RUN dotnet restore TixFlow.sln --locked-mode
RUN dotnet build tests/TixFlow.Api.Tests/TixFlow.Api.Tests.csproj -c Release --no-restore
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
CMD ["dotnet", "test", "tests/TixFlow.Api.Tests/TixFlow.Api.Tests.csproj", "-c", "Release", "--no-build", "--logger", "trx;LogFileName=tests.trx", "--results-directory", "/reports"]
