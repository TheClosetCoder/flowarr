export type DashboardSummary = {
  localStorage: StorageSummary;
  seedboxStorage: StorageSummary;
  activeDownloads: number;
  recentImports: RecentImport[];
  cleanupCandidates: CleanupCandidateSummary[];
  storageLocations: DashboardStorageLocation[];
  downloads: DashboardDownload[];
  activeTransfers: ActiveTransferSummary[];
  cleanupStats: CleanupStats;
};

export type CleanupStats = {
  pendingDuplicateCount: number;
  pendingDuplicateBytes: number;
  pendingStaleCount: number;
  pendingStaleBytes: number;
  lifetimeFreedBytes: number;
  lastScanAt?: string;
  nextScanAt?: string;
};

export type StorageSummary = { usedBytes: number; freeBytes: number };
export type RecentImport = { mediaItemId: string; title: string; importedAt: string };
export type ActiveTransferSummary = {
  id: string;
  title: string;
  status: string;
  bytesTransferred: number;
  totalBytes: number;
  createdAt: string;
};
export type BulkActionResult = { candidateId: string; ok: boolean; error?: string };
export type CleanupCandidateSummary = {
  id: string;
  path: string;
  sizeBytes: number;
  reason: string;
  confidence: string;
  dryRun: boolean;
  viewCount?: number;
  lastViewedAt?: string;
  addedAt?: string;
  seriesTitle?: string;
  seasonNumber?: number;
  episodeNumber?: number;
  mediaType?: string;
  reasonType?: "Duplicate" | "Stale";
  duplicateGroupKey?: string;
  keptPath?: string;
  isSuggestedKeep?: boolean;
  status?: string;
};
export type CleanupSettings = { staleAfterDays: number; autoGenerateEnabled: boolean; autoGenerateIntervalHours: number };
export type DashboardStorageLocation = { name: string; kind: string; pathOrEndpoint: string; usedBytes: number; freeBytes: number; lastScannedAt: string };
export type DashboardDownload = { id: string; client: string; name: string; status: string; progress: number; sizeBytes: number; remotePath?: string; updatedAt: string };
export type DownloadItem = { id: string; name: string; client: string; status: string; progress: number; sizeBytes: number; remotePath?: string };
export type StorageLocation = { id: string; name: string; kind: string | number; category: string; pathOrEndpoint: string; usedBytes: number; freeBytes: number; lastScannedAt: string };
export type RemoteTransfer = { id: string; provider: string; remotePath: string; localPath: string; currentFile?: string; bytesTransferred: number; totalBytes: number; status: string; message?: string; createdAt: string; completedAt?: string };
export type LocalFolderListing = { currentPath: string; parentPath?: string; entries: LocalFolderEntry[] };
export type LocalFolderEntry = { name: string; path: string };
export type ConnectionTestResult = { ok: boolean; message: string };
export type LocalMediaPath = { id?: string; enabled: boolean; name: string; path: string; role: string };
export type SeedboxRoot = { id?: string; enabled: boolean; name: string; kind: "Download" | "Media"; path: string };
export type TransferSettings = { enabled: boolean; localImportPath: string; overwriteExisting: boolean; allowedExtensions: string[]; localMediaPaths: LocalMediaPath[]; transferRules: TransferRule[]; sftpSeedbox: { configured: boolean; host: string; port: number; usesPrivateKey: boolean } };
export type TransferRule = { id?: string; enabled: boolean; name: string; remotePathPrefix: string; localPath: string; sourceRootKind: "Download" | "Media"; sourceRootPath: string; deleteRemoteAfterDownload: boolean };
export type ArrSettings = { enabled: boolean; baseUrl: string; hasApiKey: boolean };
export type AuthStatus = { setupComplete: boolean; authenticated: boolean };
export type JobRun = { id: string; jobType: string; status: string; startedAt: string; completedAt?: string; errorMessage?: string; progress?: string };
export type ConnectionSettings = {
  qbittorrent: { enabled: boolean; clientType: string; baseUrl: string; username: string; hasPassword: boolean };
  sftpSeedbox: { enabled: boolean; host: string; port: number; username: string; privateKeyPath: string; seedboxRoots: SeedboxRoot[]; hasPassword: boolean; hasPrivateKeyPassphrase: boolean; accountQuotaBytes: number };
  autoDownload: { enabled: boolean; localImportPath: string; overwriteExisting: boolean; allowedExtensions: string[]; localMediaPaths: LocalMediaPath[]; transferRules: TransferRule[] };
  radarr: ArrSettings;
  sonarr: ArrSettings;
  plex: ArrSettings;
  jellyfin: ArrSettings;
};

export type RawConnectionSettings = Partial<ConnectionSettings> & {
  qBittorrent?: ConnectionSettings["qbittorrent"];
  SftpSeedbox?: ConnectionSettings["sftpSeedbox"];
  sftpseedbox?: ConnectionSettings["sftpSeedbox"];
};
