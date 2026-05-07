# Flowarr

Flowarr is a self-hosted media operations dashboard for people running seedboxes, download clients, and the Arr ecosystem.

It sits above tools like qBittorrent, Radarr, Sonarr, Plex, and Jellyfin to show storage usage, download state, media lifecycle state, and cleanup recommendations in one place.

## What Flowarr Does

- Provides a Docker-based self-hosted web app.
- Exposes a .NET 8 REST API with Swagger and `/health`.
- Uses PostgreSQL through Entity Framework Core.
- Includes a React, TypeScript, and Tailwind dashboard.
- Tracks media items, downloads, media files, storage locations, cleanup candidates, and audit logs.
- Runs scheduled sync jobs through .NET hosted services and records job history.
- Shows no torrent or media integration data until real integrations are configured.
- Can auto-download completed seedbox files over SFTP into configured local media paths.
- Lets you configure Deluge or qBittorrent, SFTP seedbox, Radarr, Sonarr, and auto-download settings from the app.
- Separates seedbox download roots from imported media roots.
- Generates cleanup recommendations with manual approval and dry-run safety by default.
- Includes first-run single-admin authentication.

## What Flowarr Does Not Do

- It does not replace Radarr, Sonarr, Plex, Jellyfin, or qBittorrent.
- It does not automatically delete files.
- It does not make uncertain matches silently safe.
- It does not replace Plex or Jellyfin APIs yet.

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
FLOWARR_SONARR_BASE_URL=
FLOWARR_SONARR_API_KEY=
FLOWARR_MEDIA_ROOT=./data/media
FLOWARR_SEEDBOX_MOUNT_PATH=./data/seedbox
FLOWARR_SEEDBOX_PATH_OR_ENDPOINT=/seedbox
FLOWARR_AUTO_DOWNLOAD_ENABLED=false
FLOWARR_AUTO_DOWNLOAD_OVERWRITE_EXISTING=false
FLOWARR_SFTP_HOST=
FLOWARR_SFTP_PORT=22
FLOWARR_SFTP_USERNAME=
FLOWARR_SFTP_PASSWORD=
FLOWARR_SFTP_PRIVATE_KEY_PATH=
FLOWARR_SFTP_PRIVATE_KEY_HOST_PATH=./data/keys
FLOWARR_SFTP_REMOTE_ROOT_PATH=
```

The API also supports .NET environment variable binding:

```bash
ConnectionStrings__Flowarr=Host=postgres;Port=5432;Database=flowarr;Username=flowarr;Password=flowarr
Flowarr__Cleanup__DryRun=true
Flowarr__Cleanup__AllowDelete=false
Flowarr__Storage__LocalPath=/media
Flowarr__Storage__SeedboxPathOrEndpoint=/seedbox
Flowarr__AutoDownload__Enabled=false
Flowarr__AutoDownload__LocalImportPath=/media/import
```

## Ultra.cc / UltraSeedbox SFTP Auto-Download

Flowarr can pull completed or seeding downloads from a remote seedbox over SFTP into one or more local media paths.

You can configure Deluge or qBittorrent, SFTP seedbox, Radarr, Sonarr, and auto-download settings from the Connections page in the app. Deluge Web only needs its web password; qBittorrent can use username and password. Passwords, API keys, and key passphrases are encrypted before being stored in PostgreSQL using ASP.NET Core Data Protection. The Docker Compose file persists the encryption key ring in the `flowarr-keys` volume so saved credentials remain readable after container rebuilds.

You can also seed defaults with `.env`:

```bash
FLOWARR_AUTO_DOWNLOAD_ENABLED=true
FLOWARR_MEDIA_ROOT=/mnt/media
FLOWARR_SFTP_HOST=your-ultra-sftp-host
FLOWARR_SFTP_PORT=22
FLOWARR_SFTP_USERNAME=your-ultra-username
FLOWARR_SFTP_PASSWORD=your-ultra-password
FLOWARR_SFTP_REMOTE_ROOT_PATH=/legacy/default/root
```

In the app, configure explicit seedbox roots instead of relying on the legacy root:

- `Download` roots are the paths reported by Deluge/qBittorrent, such as `/downloads/movies`.
- `Media` roots are completed library paths managed by Radarr/Sonarr, such as `/media/Movies`.
- Auto-download transfer rules map one of those source root kinds plus a remote prefix to a local media path.

For private key auth, mount a host key folder and point Flowarr at the in-container key path:

```bash
FLOWARR_SFTP_PRIVATE_KEY_HOST_PATH=/path/on/host/keys
FLOWARR_SFTP_PRIVATE_KEY_PATH=/keys/id_rsa
FLOWARR_SFTP_PASSWORD=
```

Auto-download behavior:

- It only processes downloads marked complete or seeding.
- It can route different remote folders to different local destinations with transfer rules.
- Example: `/downloads/movies` can go to `/media/Movies`, while `/downloads/tv` can go to `/media/TV Shows`.
- It skips existing files unless `FLOWARR_AUTO_DOWNLOAD_OVERWRITE_EXISTING=true`.
- It records completed, skipped, and failed transfers in the transfer log and audit log.
- It does not delete anything from the seedbox.

For multiple disks, add extra bind mounts to the `api` service, then add matching local media paths in the app:

```yaml
volumes:
  - /mnt/disk1/Movies:/media/Movies
  - /mnt/disk2/TV Shows:/media/TV Shows
  - /mnt/archive:/media/Archive
```

## Credential Storage

- Secret fields are never returned by the API after save.
- The app returns `hasPassword` and `hasPrivateKeyPassphrase` flags instead.
- Secrets are encrypted at rest before being written to PostgreSQL.
- Keep the `flowarr-keys` Docker volume. Removing it can make existing encrypted credentials unreadable.

## Cleanup Rules

A file can be suggested for deletion only when:

- It exists on the seedbox.
- The related download is complete or seeding.
- The file has been imported locally.
- It exists in the media library or is linked to a known Radarr/Sonarr item.

Safety rules:

- No automatic deletion.
- Manual approval is required before any cleanup action.
- Dry-run mode is enabled by default.
- Real deletion intent requires `Flowarr__Cleanup__AllowDelete=true` and exact path confirmation.
- Every approval, rejection, or delete-intent is written to the audit log.
- Uncertain matches are clearly marked.

## Authentication

On first launch, Flowarr asks you to create a local admin account. The password must be at least 12 characters and is stored as a salted PBKDF2 hash. API routes require the admin session cookie except `/health` and first-run auth endpoints.

## Database Upgrades

Flowarr applies EF Core migrations at API startup. The baseline migration is upgrade-safe for existing POC databases and removes the old example mock rows if they are still present.

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

## Current Limitations

- Unconfigured integrations return no data.
- Cleanup real deletion is guarded but still intentionally conservative; keep dry-run enabled unless you have verified roots and paths.
- Plex and Jellyfin integrations remain future work.
