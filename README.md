# Flowarr

[![Publish Docker image](https://github.com/TheClosetCoder/flowarr/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/TheClosetCoder/flowarr/actions/workflows/docker-publish.yml)

Flowarr is a self-hosted media operations dashboard for people running seedboxes, download clients, and the Arr ecosystem.

It sits above tools like qBittorrent, Radarr, Sonarr, Plex, and Jellyfin to show storage usage, download state, media lifecycle state, and cleanup recommendations in one place.

## What Flowarr Does

- Ships as a single Docker container with a dark, Radarr/Sonarr-style dashboard.
- Ships a custom app icon, web manifest, and service worker so it installs as a real standalone app from a phone or tablet's browser ("Install app" / "Add to Home Screen").
- Exposes a .NET 8 REST API and `/health`, and serves the built frontend directly — no separate frontend container needed (Swagger is available in local development only).
- Uses an embedded SQLite database through Entity Framework Core — no separate database container to run.
- Includes a React, TypeScript, and Tailwind frontend.
- Tracks media items, downloads, media files, storage locations, cleanup candidates, and audit logs.
- Runs scheduled sync jobs through .NET hosted services and records job history, including live progress for long-running jobs (e.g. Plex library scans).
- Shows no torrent or media integration data until real integrations are configured.
- Can auto-download completed seedbox files over SFTP into configured local media paths, and lets you cancel a queued or in-flight transfer directly from the UI (a cancelled in-flight transfer's partial local file is deleted to reclaim space).
- Lets you configure Deluge or qBittorrent, SFTP seedbox, Radarr, Sonarr, Plex, and auto-download settings from the app.
- Separates seedbox download roots from imported media roots, and groups the Storage page by whether each folder backs a download rule or is just being monitored for free space.
- Reports accurate free space for both local disks (resolves the actual mount backing a path, not just the container's root filesystem) and, optionally, your real SFTP account quota instead of the shared seedbox host's total disk size.
- Generates local-disk cleanup recommendations from your Plex watch history (unwatched/stale titles, duplicate versions of the same title) with manual approval and dry-run safety by default.
- Includes first-run single-admin authentication.

## What Flowarr Does Not Do

- It does not replace Radarr, Sonarr, Plex, or qBittorrent.
- It does not automatically delete files.
- It does not make uncertain matches silently safe.
- It does not integrate with Jellyfin yet.

## Quick Start

Flowarr ships as a single prebuilt image published to GitHub Container Registry — no build step, no separate database container:

```bash
docker run -d \
  --name flowarr \
  -p 8080:8080 \
  -v /mnt/media:/media \
  -v flowarr-data:/var/lib/flowarr/data \
  -v flowarr-keys:/var/lib/flowarr/keys \
  ghcr.io/theclosetcoder/flowarr:latest
```

Open `http://localhost:8080` and complete first-run admin setup. `flowarr-data` holds the SQLite database and `flowarr-keys` holds the credential-encryption key ring — keep both volumes across upgrades. Add further `-v /mnt/diskN/Folder:/media/Folder` mounts for additional media disks.

To pin a version instead of following `latest`, use a tag from the [Packages page](https://github.com/TheClosetCoder/flowarr/pkgs/container/flowarr) (e.g. `ghcr.io/theclosetcoder/flowarr:v1.2.0`).

## Run With Docker Compose

Prefer Compose if you want `.env`-driven integration settings (qBittorrent, Radarr, Sonarr, SFTP) instead of one-off `-e` flags. From the repository root:

```bash
cd docker
cp .env.example .env
nano .env
docker compose -f docker/docker-compose.yml up --build
```

Set `FLOWARR_MEDIA_ROOT` in `docker/.env` to your host media folder, for example:

```env
FLOWARR_MEDIA_ROOT=/mnt/media
```

Inside Flowarr, use the container paths under `/media`, for example `/media/Movies` or `/media/TV Shows`.

Open `http://localhost:8080` for the dashboard, health at `http://localhost:8080/health`.

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
```

The API also supports .NET environment variable binding:

```bash
ConnectionStrings__Flowarr=Data Source=/var/lib/flowarr/data/flowarr.db
Flowarr__Cleanup__DryRun=true
Flowarr__Cleanup__AllowDelete=false
Flowarr__Storage__LocalPath=/media
Flowarr__Storage__SeedboxPathOrEndpoint=/seedbox
Flowarr__AutoDownload__Enabled=false
Flowarr__AutoDownload__LocalImportPath=/media/import
```

## Ultra.cc / UltraSeedbox SFTP Auto-Download

Flowarr can pull completed or seeding downloads from a remote seedbox over SFTP into one or more local media paths.

You can configure Deluge or qBittorrent, SFTP seedbox, Radarr, Sonarr, Plex, and auto-download settings from the Connections page in the app. Deluge Web only needs its web password; qBittorrent can use username and password. Passwords, API keys, and key passphrases are encrypted before being stored in the SQLite database using ASP.NET Core Data Protection. The Docker Compose file persists the encryption key ring in the `flowarr-keys` volume and the database file in the `flowarr-data` volume so saved credentials and data remain readable after container rebuilds.

You can also seed defaults with `.env`:

```bash
FLOWARR_AUTO_DOWNLOAD_ENABLED=true
FLOWARR_MEDIA_ROOT=/mnt/media
FLOWARR_SFTP_HOST=your-ultra-sftp-host
FLOWARR_SFTP_PORT=22
FLOWARR_SFTP_USERNAME=your-ultra-username
FLOWARR_SFTP_PASSWORD=your-ultra-password
```

In the app, configure at least one seedbox root under SFTP settings — quota, storage, and download features all depend on it:

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
- It skips existing files unless `FLOWARR_AUTO_DOWNLOAD_OVERWRITE_EXISTING=true` — and if a local file's size doesn't match the remote (e.g. a partial file left by an interrupted transfer), it re-downloads it regardless of that setting, since a mismatched size is never a legitimately "already downloaded" file.
- It records completed, skipped, failed, and cancelled transfers in the transfer log and audit log.
- Queued and in-flight transfers can be cancelled from the Transfers page or the Dashboard; cancelling an in-flight transfer deletes its partial local file.
- It does not delete anything from the seedbox unless a transfer rule has "Delete remote after download" enabled.

By default, the Storage page reports free space from the SFTP server's `statvfs` response, which on a shared host is the *whole server's* free space, not your personal allocation. If your provider gives you a fixed quota, set it under Connections → SFTP → "Account quota (GB)" — Flowarr will then report `quota - total account usage` (computed via one periodic scan of the seedbox root, cached for ~15 minutes) as the free-space figure across all your seedbox entries instead.

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
- Secrets are encrypted at rest before being written to the database.
- Keep the `flowarr-keys` Docker volume. Removing it can make existing encrypted credentials unreadable.

## Cleanup Rules

Cleanup targets **local disk space**, not the seedbox (the seedbox is assumed to be temporary storage, not something Cleanup needs to manage). It requires a Plex connection (Connections → Services → Plex, with a base URL and token) — Plex's own library data gives Flowarr the exact local file path, watch count, last-viewed date, and (via multiple `Media` entries under one item) a built-in signal for duplicate physical copies of the same title, without needing Radarr/Sonarr at all.

Clicking "Generate recommendations" runs as a background job and shows live progress in the UI — Plex requires one API call per TV series to fetch episode data, so a large TV library can take a while.

A local file is suggested for cleanup when:

- **Duplicate version**: Plex has matched multiple physical copies to the same title (multiple `Media` entries on one item). The largest copy is kept; the rest are flagged (`Certain` confidence).
- **Stale**: never watched and added more than the configured "stale after" threshold ago, or watched but not viewed again within that threshold (default 90 days, configurable on the Cleanup page; `Certain` confidence).
- **Possible duplicate title**: the same normalized title appears under separate Plex entries that Plex itself did not merge (`Uncertain` confidence — needs manual review, since this is a weaker signal than the Media-group case above).

Safety rules:

- No automatic deletion.
- Manual approval is required before any cleanup action.
- Dry-run mode is enabled by default.
- Real deletion requires `Flowarr__Cleanup__AllowDelete=true`, exact path confirmation (typed into the UI, must match the candidate's path exactly), and the path must fall under a configured seedbox root or local media path.
- Every approval, rejection, or delete is written to the audit log.
- Uncertain matches are clearly marked.

Note: candidate paths come from Plex's own view of the filesystem, so Plex and Flowarr need to see the same file paths (e.g. both mounting `/media` the same way) for candidates to resolve to real, deletable files — Flowarr silently skips anything it can't verify exists rather than presenting an unusable candidate.

## App Icon & Add to Home Screen

Flowarr uses a custom "converge" icon (three sources merging into a download tray) instead of a generic favicon, defined once as a scalable source and reused everywhere:

- `frontend/public/icon.svg` — the master vector icon, used as the browser-tab favicon in browsers that support SVG favicons.
- `frontend/public/icons/favicon-16.png` and `favicon-32.png` — PNG favicon fallbacks for browsers that don't.
- `frontend/public/icons/apple-touch-icon.png` (180×180) — used by iOS Safari's "Add to Home Screen".
- `frontend/public/icons/icon-192.png` and `icon-512.png`, referenced from `frontend/public/manifest.webmanifest` — used by Android/Chrome's "Add to Home Screen" / install prompt.
- `frontend/src/components/FlowarrMark.tsx` — the same glyph as an inline SVG component, used for the in-app sidebar and login-screen logo so it matches the installed icon.

All of this is wired up via `<link>`/`<meta>` tags in `frontend/index.html` (favicon, `apple-touch-icon`, `manifest`, `theme-color`) — nothing further to configure. To change the icon, edit `frontend/public/icon.svg` and `FlowarrMark.tsx` to match, then regenerate the PNGs in `frontend/public/icons/` at the same sizes (16, 32, 48, 180, 192, 512px) with any SVG-to-PNG tool, keeping the existing filenames.

## Authentication

On first launch, Flowarr asks you to create a local admin account. The password must be at least 12 characters and is stored as a salted PBKDF2 hash. API routes require the admin session cookie except `/health` and first-run auth endpoints. Complete this first-run setup on your local network before exposing the instance to the internet.

## Exposing Flowarr to the Internet

Flowarr itself serves plain HTTP with no TLS. Don't forward its port directly to the internet — put a TLS-terminating reverse proxy in front of it instead (nginx, Caddy, Traefik, or a Cloudflare Tunnel). Flowarr trusts `X-Forwarded-For`/`X-Forwarded-Proto` headers from any source, so make sure inbound internet traffic can only reach it through that proxy (e.g. a Cloudflare Tunnel with no router port-forward, or a firewall rule that only allows the reverse proxy's host/network to reach the published port).

## Database Upgrades

Flowarr uses an embedded SQLite database (`flowarr.db`, persisted via the `flowarr-data` Docker volume) and applies EF Core migrations at API startup — no separate database container or manual setup required.

**Never edit a migration after it has shipped.** EF Core tracks which migration IDs have already run per database in `__EFMigrationsHistory`; editing an already-applied migration's `Up()` has no effect on environments that already ran it, since `Migrate()` only checks the ID, not the file contents. Add a new migration instead, generated with `dotnet dotnet-ef migrations add <Name> --project backend/Flowarr.Infrastructure --startup-project backend/Flowarr.Api -o Migrations`.

## Deploying to a Remote Host

`scripts/deploy-to-proxmox.ps1` is a PowerShell script for redeploying to a host reachable over SSH (written for a Proxmox host but not Proxmox-specific): it locates the `flowarr` checkout on the remote host automatically, runs `git pull`, rebuilds the Docker images, and restarts the containers. Run it from a PowerShell prompt in the repo root:

```powershell
./scripts/deploy-to-proxmox.ps1 -ProxmoxHost <host-or-ip> -User <ssh-user>
```

It prompts for the SSH password interactively and never stores credentials. If the remote's `origin` is a private GitHub repo, set up a dedicated deploy key on that host (a fresh keypair added as a read-only GitHub Deploy Key) rather than reusing any existing host identity key.

## Project Structure

```text
backend/
  Flowarr.Api/
  Flowarr.Core/
  Flowarr.Infrastructure/
frontend/
docker/
  docker-compose.yml
scripts/
  deploy-to-proxmox.ps1
```

## Current Limitations

- Unconfigured integrations return no data.
- Cleanup real deletion is guarded but still intentionally conservative; keep dry-run enabled unless you have verified roots and paths.
- Jellyfin integration remains future work (Plex is supported).
- Cleanup candidate paths depend on Plex and Flowarr resolving the same file paths (see Cleanup Rules above).
