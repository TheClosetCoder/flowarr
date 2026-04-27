# Flowarr

Flowarr is a self-hosted media operations dashboard for people running seedboxes, download clients, and the Arr ecosystem.

It sits above tools like qBittorrent, Radarr, Sonarr, Plex, and Jellyfin to show storage usage, download state, media lifecycle state, and cleanup recommendations in one place.

## What Flowarr Does

- Provides a Docker-based self-hosted web app.
- Exposes a .NET 8 REST API with Swagger and `/health`.
- Uses PostgreSQL through Entity Framework Core.
- Includes a React, TypeScript, and Tailwind dashboard.
- Tracks media items, downloads, media files, storage locations, cleanup candidates, and audit logs.
- Runs scheduled sync jobs through a .NET hosted service.
- Starts with mock qBittorrent and Radarr integrations when credentials are not configured.
- Generates cleanup recommendations without deleting automatically.

## What Flowarr Does Not Do

- It does not replace Radarr, Sonarr, Plex, Jellyfin, or qBittorrent.
- It does not automatically delete files in the MVP.
- It does not make uncertain matches silently safe.
- It does not yet implement real qBittorrent, Radarr, Sonarr, Plex, or Jellyfin API clients.
- It does not yet implement a production authentication model.

## Run With Docker Compose

From the repository root:

```bash
docker compose -f docker/docker-compose.yml up --build
```

Open:

- Frontend: `http://localhost:5173`
- API Swagger: `http://localhost:8080/swagger`
- Health: `http://localhost:8080/health`

## Initial Environment Variables

Docker Compose reads these optional variables:

```bash
FLOWARR_QBITTORRENT_BASE_URL=
FLOWARR_QBITTORRENT_USERNAME=
FLOWARR_QBITTORRENT_PASSWORD=
FLOWARR_RADARR_BASE_URL=
FLOWARR_RADARR_API_KEY=
FLOWARR_LOCAL_MEDIA_PATH=./data/media
FLOWARR_SEEDBOX_MOUNT_PATH=./data/seedbox
FLOWARR_SEEDBOX_PATH_OR_ENDPOINT=/seedbox
```

The API also supports .NET environment variable binding:

```bash
ConnectionStrings__Flowarr=Host=postgres;Port=5432;Database=flowarr;Username=flowarr;Password=flowarr
Flowarr__Cleanup__DryRun=true
Flowarr__Storage__LocalPath=/media
Flowarr__Storage__SeedboxPathOrEndpoint=/seedbox
```

## MVP Cleanup Rules

A file can be suggested for deletion only when:

- It exists on the seedbox.
- The related download is complete or seeding.
- The file has been imported locally.
- It exists in the media library or is linked to a known Radarr item.

Safety rules:

- No automatic deletion in the MVP.
- Manual approval is required before any cleanup action.
- Dry-run mode is enabled by default.
- Every approval, rejection, or delete-intent is written to the audit log.
- Uncertain matches are clearly marked.

## Project Structure

```text
backend/
  Flowarr.Api/
  Flowarr.Core/
  Flowarr.Infrastructure/
frontend/
docker/
  docker-compose.yml
```

## Current MVP Limitations

- Integrations are mocked until real credentials and adapters are implemented.
- Database schema is created with `EnsureCreated` for MVP bootstrapping, not migrations.
- Cleanup approval logs delete intent, but no production file-deletion adapter is enabled.
- The frontend is a first-pass dashboard and does not yet include full connection testing or auth.
