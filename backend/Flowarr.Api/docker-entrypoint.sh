#!/bin/sh
set -e

# Started as root (the default): take ownership of Flowarr's own state, then drop to PUID:PGID for
# the app itself. The chown also migrates volumes created by older images, which ran as root.
# Started unprivileged (docker run --user, Kubernetes runAsUser): there is nothing to fix up.
if [ "$(id -u)" = "0" ]; then
    PUID="${PUID:-1000}"
    PGID="${PGID:-1000}"
    if [ "$PUID" = "0" ]; then
        echo "Refusing to run Flowarr as root (PUID=0). Set PUID/PGID to the owner of your media folders." >&2
        exit 1
    fi

    mkdir -p /var/lib/flowarr/data /var/lib/flowarr/keys
    chown -R "$PUID:$PGID" /var/lib/flowarr
    export HOME=/tmp
    exec setpriv --reuid="$PUID" --regid="$PGID" --clear-groups -- dotnet Flowarr.Api.dll "$@"
fi

exec dotnet Flowarr.Api.dll "$@"
