# syntax=docker/dockerfile:1
# The Azwah identity provider image: the platform's OpenID Connect provider, brokering UAE PASS staging (D-129).
# It is separate from the API image, which never contains it.
# Build context is the backend folder, like the API's image:
#   docker build -f FamilyResilience.IdentityProvider/Dockerfile .   (from backend/)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until the project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY FamilyResilience.IdentityProvider/FamilyResilience.IdentityProvider.csproj FamilyResilience.IdentityProvider/
RUN dotnet restore FamilyResilience.IdentityProvider/FamilyResilience.IdentityProvider.csproj

COPY FamilyResilience.IdentityProvider/ FamilyResilience.IdentityProvider/
RUN dotnet publish FamilyResilience.IdentityProvider/FamilyResilience.IdentityProvider.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# The aspnet image ships a non-root "app" user; APP_UID is its numeric id.
USER $APP_UID
# No ASPNETCORE_ENVIRONMENT here on purpose: the deployment sets it. Development (Docker Compose sets it for local
# development) offers simulated personas and UAE PASS staging; anything else is the deployed, UAE PASS-only mode,
# which refuses to start without an https issuer, a signing key and the UAE PASS credentials.
ENV ASPNETCORE_HTTP_PORTS=5090
EXPOSE 5090
ENTRYPOINT ["dotnet", "FamilyResilience.IdentityProvider.dll"]
