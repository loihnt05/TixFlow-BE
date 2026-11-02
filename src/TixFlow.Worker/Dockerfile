FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore TixFlow.sln --locked-mode
RUN dotnet publish src/TixFlow.Worker/TixFlow.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/ticket-keys && chown -R app:app /app
USER app
ENTRYPOINT ["dotnet", "TixFlow.Worker.dll"]
