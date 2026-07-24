# Security Policy

## Supported Versions

Flowarr is a solo-maintained project. Only the latest released version receives
security fixes — there are no backports to older tags. If you're running a pinned
version, upgrading to the current release is the fix path.

| Version        | Supported |
| -------------- | --------- |
| Latest release | Yes       |
| Anything older | No        |

## Reporting a Vulnerability

**Please do not open a public issue, Discord message, or Reddit post for security
reports.**

Use GitHub's private vulnerability reporting:
[Report a vulnerability](https://github.com/TheClosetCoder/flowarr/security/advisories/new)
(repository → **Security** tab → **Report a vulnerability**). This keeps the
report private until a fix is out.

Please include:

- What the issue is and which component it affects (API endpoint, SFTP transfer,
  cleanup path handling, credential storage, auth, frontend).
- Steps to reproduce, ideally against a throwaway instance.
- Flowarr version or image tag, and your deployment shape (direct, reverse proxy,
  Cloudflare Tunnel).
- Impact as you see it.

Do not include real credentials, API keys, tokens, or seedbox passwords in a
report. Reproduce with test values.

### What to expect

This is a hobby project maintained by one person, so no formal SLA — but in
practice: acknowledgement within about 7 days, an assessment of severity and a
rough fix timeline after that, and a note when the fix ships. If you've heard
nothing in 14 days, please ping the advisory thread; it means it got missed, not
ignored.

There is no bug bounty and no payment. Credit in the release notes and the
advisory if you'd like it, or anonymity if you'd prefer that.

## In Scope

Things I want to hear about:

- Authentication bypass, session fixation, or privilege issues in the admin login.
- Any path by which stored credentials (SFTP passwords, API keys, passphrases) can
  be read back through the API, logs, or the database in usable form.
- Path traversal or validation gaps that let cleanup or transfer operations touch
  files outside configured seedbox roots and local media paths.
- SSRF or request smuggling through the configurable integration URLs
  (Deluge/qBittorrent, Radarr, Sonarr, Plex).
- Stored or reflected XSS, CSRF against state-changing endpoints, or injection into
  SQL/shell/SFTP paths.
- Secrets leaking into logs, audit entries, API error responses, or the built
  frontend bundle.
- Container issues such as unnecessary privilege or writable paths that enable
  escape.

## Out of Scope

These are known, documented design decisions rather than vulnerabilities:

- **No TLS.** Flowarr serves plain HTTP by design and expects a TLS-terminating
  reverse proxy in front of it. See "Exposing Flowarr to the Internet" in the README.
- **`X-Forwarded-For` / `X-Forwarded-Proto` are trusted from any source.** This is
  documented, and the mitigation is network-level: only your proxy should be able
  to reach the published port.
- **The admin account is fully privileged.** Flowarr has a single-admin model, so
  "an authenticated admin can configure arbitrary paths, URLs, or credentials" is
  the intended behavior, not an escalation.
- Findings that require an attacker to already have host, Docker socket, or volume
  access.
- Automated scanner output with no demonstrated impact, and vulnerable-dependency
  reports with no reachable code path in Flowarr. A reachable path makes it in
  scope — please show it.
- Denial of service caused by your own configuration (e.g. pointing a sync job at
  an enormous library).

## Deployment Assumptions

Flowarr is built for a self-hosted LAN or reverse-proxied deployment with a single
trusted admin. Do not expose it directly to the internet, and complete first-run
admin setup on a trusted network. Keep the `flowarr-keys` volume: it holds the
key ring that decrypts stored credentials.

## Disclosure

I'd ask for coordinated disclosure — please give me a chance to ship a fix before
publishing. 90 days is a reasonable ceiling; for anything actively exploitable I'll
aim much faster than that. I'll publish a GitHub advisory when the fix lands.

## A Note on How This Code Is Reviewed

Flowarr is developed with extensive AI assistance, and code review is primarily
done with Claude — see [How Flowarr Is Built](../README.md#how-flowarr-is-built).
Security-sensitive paths (auth, credential handling, deletion, path validation) are
the specific cases I read through by hand. I'm telling you this because it's
relevant to how you should weigh your own findings: report the thing you found
rather than assuming it must already have been considered.
