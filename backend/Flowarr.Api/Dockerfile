FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY NuGet.Config ./
COPY backend/Flowarr.Core/Flowarr.Core.csproj backend/Flowarr.Core/
COPY backend/Flowarr.Infrastructure/Flowarr.Infrastructure.csproj backend/Flowarr.Infrastructure/
COPY backend/Flowarr.Api/Flowarr.Api.csproj backend/Flowarr.Api/
RUN dotnet restore backend/Flowarr.Api/Flowarr.Api.csproj

COPY backend ./backend
RUN dotnet publish backend/Flowarr.Api/Flowarr.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
COPY --from=frontend-build /src/frontend/dist ./wwwroot
COPY backend/Flowarr.Api/docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
# The entrypoint drops from root to PUID:PGID with setpriv; fail the build rather than the container if it's missing.
RUN chmod 755 /usr/local/bin/docker-entrypoint.sh && command -v setpriv
ENV PUID=1000 \
    PGID=1000
EXPOSE 8080
ENTRYPOINT ["docker-entrypoint.sh"]
