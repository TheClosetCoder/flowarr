import { createClientId } from "./api";
import type { ConnectionSettings, RawConnectionSettings } from "./types";

export function normalizeAutoDownload(autoDownload: Partial<ConnectionSettings["autoDownload"]> | undefined): ConnectionSettings["autoDownload"] {
  return {
    enabled: autoDownload?.enabled ?? false,
    localImportPath: autoDownload?.localImportPath ?? "/media/import",
    overwriteExisting: autoDownload?.overwriteExisting ?? false,
    allowedExtensions: autoDownload?.allowedExtensions ?? [".mkv", ".mp4", ".avi", ".mov", ".m4v", ".srt"],
    localMediaPaths: (autoDownload?.localMediaPaths ?? []).map((path) => ({ ...path, id: path.id ?? createClientId() })),
    transferRules: (autoDownload?.transferRules ?? []).map((rule) => ({ ...rule, sourceRootKind: rule.sourceRootKind ?? "Download", sourceRootPath: rule.sourceRootPath ?? "", deleteRemoteAfterDownload: rule.deleteRemoteAfterDownload ?? false, id: rule.id ?? createClientId() }))
  };
}

export function defaultConnectionSettings(): ConnectionSettings {
  return {
    qbittorrent: {
      enabled: false,
      clientType: "Deluge",
      baseUrl: "",
      username: "",
      hasPassword: false
    },
    sftpSeedbox: {
      enabled: false,
      host: "",
      port: 22,
      username: "",
      privateKeyPath: "",
      seedboxRoots: [],
      hasPassword: false,
      hasPrivateKeyPassphrase: false,
      accountQuotaBytes: 0
    },
    autoDownload: normalizeAutoDownload(undefined),
    radarr: { enabled: false, baseUrl: "", hasApiKey: false },
    sonarr: { enabled: false, baseUrl: "", hasApiKey: false },
    plex: { enabled: false, baseUrl: "", hasApiKey: false }
  };
}

export function normalizeConnectionSettings(settings: RawConnectionSettings): ConnectionSettings {
  const defaults = defaultConnectionSettings();
  return {
    qbittorrent: settings.qbittorrent ?? settings.qBittorrent ?? defaults.qbittorrent,
    sftpSeedbox: settings.sftpSeedbox ?? settings.SftpSeedbox ?? settings.sftpseedbox ?? defaults.sftpSeedbox,
    autoDownload: normalizeAutoDownload(settings.autoDownload),
    radarr: settings.radarr ?? defaults.radarr,
    sonarr: settings.sonarr ?? defaults.sonarr,
    plex: settings.plex ?? defaults.plex
  };
}
