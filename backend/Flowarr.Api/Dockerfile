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
EXPOSE 8080
ENTRYPOINT ["dotnet", "Flowarr.Api.dll"]
