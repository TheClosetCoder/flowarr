# Contributing to Flowarr

Thanks for taking an interest. Flowarr is a solo-maintained hobby project, so
please read this before spending real time on a change — a little coordination up
front saves everyone a wasted evening.

## How This Project Is Developed

Flowarr is built with extensive AI assistance; see
[How Flowarr Is Built](../README.md#how-flowarr-is-built) for the full picture.
Two consequences worth knowing before you contribute:

- **Your pull request will be reviewed with AI assistance**, plus a manual pass on
  anything touching auth, credentials, deletion, or path handling.
- **AI-assisted contributions are welcome** — it would be strange to say otherwise
  here. The one firm rule: don't open a pull request for code you haven't run.
  Generated code that was never executed against a real instance costs more to
  review than it saves, and it will be closed.

## Ways to Contribute

**Bug reports** are the most useful thing you can send. Please include:

- Flowarr version or image tag, and how you're running it (Docker, Compose, bare).
- Which integrations are configured (torrent client, SFTP host, Radarr/Sonarr, Plex).
- What you expected, what happened, and the relevant container logs.
- Redact credentials, API keys, and tokens before pasting logs or config.

**Feature ideas** belong on the [roadmap board](https://github.com/users/TheClosetCoder/projects/1)
or in [Discord](https://discord.gg/pEGw7aN53T) first, where they can be discussed and
voted on. See [ROADMAP.md](../ROADMAP.md) for what's already planned.

**Security issues** must not go in a public issue or pull request. Follow
[SECURITY.md](SECURITY.md) instead.

**Pull requests**: for anything beyond a typo, a small bug fix, or a docs
correction, open an issue first and get a "yes, that fits" before you build it.
Flowarr has an opinionated scope, and a well-built feature that doesn't belong in
it is a painful thing to have to turn down.

## Development Setup

Prerequisites: **.NET 8 SDK** and **Node 22** (what the Docker build uses; Node 20+
should work fine).

Run the API — pass `--urls` explicitly, because the frontend dev server proxies to
port 8080 while the default launch profile uses 5084:

```bash
dotnet run --project backend/Flowarr.Api --urls http://localhost:8080
```

In a second terminal, run the frontend dev server (Vite on port 5173, proxying
`/api` and `/health` to the API above):

```bash
cd frontend
npm install
npm run dev
```

The API applies EF Core migrations to a local SQLite file at startup, so there's no
database setup step. Swagger is available at `/swagger` in development only.

To exercise the real shipped artifact — single container, built frontend served by
the API — use Compose instead:

```bash
docker compose -f docker/docker-compose.yml up --build
```

## Testing Your Change

There is no automated test suite today. Verification is manual, against a real
setup — that's the honest state of things, and it's why the bar for "I ran this"
is high.

Before opening a pull request:

- Confirm both build steps pass: `dotnet build` at the repository root and
  `npm run build` in `frontend/` (the latter type-checks via `tsc -b`).
- Exercise the actual code path against real services where you can. If you changed
  SFTP transfers, run a transfer. If you changed cleanup, generate recommendations
  against a real Plex library with dry-run on.
- Say in the pull request what you ran it against — client, provider, library size,
  whatever is relevant. This is the single most valuable thing in the description.

If you can't test a path (no seedbox, no Plex), say so rather than guessing. A
change with a clearly stated gap is reviewable; one that implies coverage it doesn't
have is not.

## Database Migrations

**Never edit a migration that has already shipped.** EF Core records applied
migration IDs in `__EFMigrationsHistory` and only checks the ID, not the file
contents, so an edit silently does nothing on any instance that already ran it —
including every user upgrading from a published image. Add a new migration:

```bash
dotnet dotnet-ef migrations add <Name> --project backend/Flowarr.Infrastructure --startup-project backend/Flowarr.Api -o Migrations
```

Call out any migration in your pull request description.

## Code Style

- Match the surrounding code. The existing patterns in each project are the style
  guide; there's no separate linter or formatter to satisfy.
- Keep the layering intact: `Flowarr.Core` holds entities, models, and integration
  contracts; `Flowarr.Infrastructure` holds EF Core, integration implementations,
  and options; `Flowarr.Api` holds controllers and startup wiring.
- Comments should explain *why*, not restate the code. The existing non-obvious
  comments (workflow anti-recursion, service worker fallbacks, migration IDs) are
  the model.
- **Discuss new dependencies before adding them.** Flowarr ships as one small
  container with a deliberately lean dependency list, and that's a feature.
- Never log or return secrets. Credential fields are write-only through the API —
  it returns `hasPassword`-style flags instead of values, and new secret fields must
  follow that pattern.

## Pull Requests

- Branch from `main` and target `main`.
- Keep it focused. One change per pull request; unrelated cleanups make review
  slower and riskier.
- Write imperative commit subjects ("Add SFTP quota reporting", not "added" or
  "adds"), with a body explaining *why* when it isn't obvious.
- Describe the user-visible effect, and note any new configuration, environment
  variable, or migration.
- Update `README.md` when you change configuration, behavior, or safety rules. The
  README is the user manual and Docker Hub description; stale docs are treated as
  a bug.
- Don't bump versions or edit tags. Releases are cut by the maintainer through the
  "Cut a release" workflow, which tags and publishes images automatically.

Expect review to take a while sometimes — this is one person's spare time, not a
support contract. Nudging an untouched pull request after a week or two is fine.

## License

Flowarr is licensed under the [Apache License, Version 2.0](../LICENSE). By
contributing, you agree that your contributions are licensed under the same terms.
