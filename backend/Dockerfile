# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY FamilyResilience.Domain/FamilyResilience.Domain.csproj FamilyResilience.Domain/
COPY FamilyResilience.Application/FamilyResilience.Application.csproj FamilyResilience.Application/
COPY FamilyResilience.Infrastructure/FamilyResilience.Infrastructure.csproj FamilyResilience.Infrastructure/
COPY FamilyResilience.Api/FamilyResilience.Api.csproj FamilyResilience.Api/
RUN dotnet restore FamilyResilience.Api/FamilyResilience.Api.csproj

COPY FamilyResilience.Domain/ FamilyResilience.Domain/
COPY FamilyResilience.Application/ FamilyResilience.Application/
COPY FamilyResilience.Infrastructure/ FamilyResilience.Infrastructure/
COPY FamilyResilience.Api/ FamilyResilience.Api/
RUN dotnet publish FamilyResilience.Api/FamilyResilience.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# The aspnet image ships a non-root "app" user; APP_UID is its numeric id.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FamilyResilience.Api.dll"]
