import React from "react";
import { createRoot } from "react-dom/client";
import { Activity, Check, ChevronUp, Download, FolderDown, FolderOpen, HardDrive, Info, LogOut, Plug, Settings, ShieldCheck, Trash2, X } from "lucide-react";
import "./styles.css";

type DashboardSummary = {
  localStorage: StorageSummary;
  seedboxStorage: StorageSummary;
  activeDownloads: number;
  recentImports: RecentImport[];
  cleanupCandidates: CleanupCandidateSummary[];
  storageLocations: DashboardStorageLocation[];
  downloads: DashboardDownload[];
};

type StorageSummary = { usedBytes: number; freeBytes: number };
type RecentImport = { mediaItemId: string; title: string; importedAt: string };
type CleanupCandidateSummary = { id: string; path: string; sizeBytes: number; reason: string; confidence: string; dryRun: boolean };
type DashboardStorageLocation = { name: string; kind: string; pathOrEndpoint: string; usedBytes: number; freeBytes: number; lastScannedAt: string };
type DashboardDownload = { id: string; client: string; name: string; status: string; progress: number; sizeBytes: number; remotePath?: string; updatedAt: string };
type DownloadItem = { id: string; name: string; client: string; status: string; progress: number; sizeBytes: number; remotePath?: string };
type StorageLocation = { id: string; name: string; kind: string | number; pathOrEndpoint: string; usedBytes: number; freeBytes: number; lastScannedAt: string };
type RemoteTransfer = { id: string; provider: string; remotePath: string; localPath: string; bytesTransferred: number; totalBytes: number; status: string; message?: string; createdAt: string; completedAt?: string };
type LocalFolderListing = { currentPath: string; parentPath?: string; entries: LocalFolderEntry[] };
type LocalFolderEntry = { name: string; path: string };
type ConnectionTestResult = { ok: boolean; message: string };
type LocalMediaPath = { id?: string; enabled: boolean; name: string; path: string; role: string };
type SeedboxRoot = { id?: string; enabled: boolean; name: string; kind: "Download" | "Media"; path: string };
type TransferSettings = { enabled: boolean; localImportPath: string; overwriteExisting: boolean; allowedExtensions: string[]; localMediaPaths: LocalMediaPath[]; transferRules: TransferRule[]; sftpSeedbox: { configured: boolean; host: string; port: number; remoteRootPath: string; usesPrivateKey: boolean } };
type TransferRule = { id?: string; enabled: boolean; name: string; remotePathPrefix: string; localPath: string; sourceRootKind: "Download" | "Media"; sourceRootPath: string };
type ArrSettings = { enabled: boolean; baseUrl: string; hasApiKey: boolean };
type AuthStatus = { setupComplete: boolean; authenticated: boolean };
type JobRun = { id: string; jobType: string; status: string; startedAt: string; completedAt?: string; errorMessage?: string };
type ConnectionSettings = {
  qbittorrent: { enabled: boolean; clientType: string; baseUrl: string; username: string; hasPassword: boolean };
  sftpSeedbox: { enabled: boolean; host: string; port: number; username: string; privateKeyPath: string; remoteRootPath: string; seedboxRoots: SeedboxRoot[]; hasPassword: boolean; hasPrivateKeyPassphrase: boolean };
  autoDownload: { enabled: boolean; localImportPath: string; overwriteExisting: boolean; allowedExtensions: string[]; localMediaPaths: LocalMediaPath[]; transferRules: TransferRule[] };
  radarr: ArrSettings;
  sonarr: ArrSettings;
};

type RawConnectionSettings = Partial<ConnectionSettings> & {
  qBittorrent?: ConnectionSettings["qbittorrent"];
  SftpSeedbox?: ConnectionSettings["sftpSeedbox"];
  sftpseedbox?: ConnectionSettings["sftpSeedbox"];
};

const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";
const pageSlugs: Record<string, string> = {
  Dashboard: "dashboard",
  Connections: "connections",
  Storage: "storage",
  Downloads: "downloads",
  Transfers: "transfers",
  Cleanup: "cleanup",
  Settings: "settings"
};
const pageBySlug = Object.fromEntries(Object.entries(pageSlugs).map(([page, slug]) => [slug, page]));
const connectionTabs = ["torrent", "sftp", "services", "plex", "jellyfin"];

function formatBytes(value: number) {
  if (!value) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${(value / 1024 ** index).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function getRouteFromHash() {
  const [pageSlug = "dashboard", tabSlug = "torrent"] = window.location.hash.replace(/^#\/?/, "").split("/");
  const page = pageBySlug[pageSlug.toLowerCase()] ?? "Dashboard";
  const connectionTab = connectionTabs.includes(tabSlug?.toLowerCase()) ? tabSlug.toLowerCase() : "torrent";
  return { page, connectionTab };
}

function navigateToPage(page: string, currentConnectionTab: string) {
  const pageSlug = pageSlugs[page] ?? "dashboard";
  const tab = page === "Connections" ? `/${currentConnectionTab}` : "";
  window.location.hash = `${pageSlug}${tab}`;
}

function navigateToConnectionTab(tab: string) {
  window.location.hash = `connections/${tab}`;
}

function App() {
  const [auth, setAuth] = React.useState<AuthStatus | null>(null);
  const initialRoute = getRouteFromHash();
  const [activePage, setActivePage] = React.useState(initialRoute.page);
  const [connectionTab, setConnectionTab] = React.useState(initialRoute.connectionTab);
  const [dashboard, setDashboard] = React.useState<DashboardSummary | null>(null);
  const [downloads, setDownloads] = React.useState<DownloadItem[]>([]);
  const [storage, setStorage] = React.useState<StorageLocation[]>([]);
  const [candidates, setCandidates] = React.useState<CleanupCandidateSummary[]>([]);
  const [transfers, setTransfers] = React.useState<RemoteTransfer[]>([]);
  const [jobs, setJobs] = React.useState<JobRun[]>([]);
  const [transferSettings, setTransferSettings] = React.useState<TransferSettings | null>(null);
  const [connectionSettings, setConnectionSettings] = React.useState<ConnectionSettings | null>(null);
  const [settingsError, setSettingsError] = React.useState("");

  const refreshSettings = React.useCallback(async () => {
    setSettingsError("");
    const connectionsPromise = fetchJson<RawConnectionSettings>("/api/connections/settings")
      .then((connectionsSettings) => setConnectionSettings(normalizeConnectionSettings(connectionsSettings)));
    const transfersPromise = fetchJson<TransferSettings>("/api/transfers/settings")
      .then(setTransferSettings);

    const results = await Promise.allSettled([connectionsPromise, transfersPromise]);
    const failed = results.find((result) => result.status === "rejected");
    if (failed?.status === "rejected") {
      setSettingsError(failed.reason instanceof Error ? failed.reason.message : "Unable to load some settings.");
    }
  }, []);

  React.useEffect(() => {
    void fetchJson<AuthStatus>("/api/auth/status")
      .then(setAuth)
      .catch(() => setAuth({ setupComplete: false, authenticated: false }));
  }, []);

  React.useEffect(() => {
    if (!auth?.authenticated) {
      return;
    }

    void Promise.all([
      fetchJson<DashboardSummary>("/api/dashboard").then(setDashboard),
      fetchJson<DownloadItem[]>("/api/downloads").then(setDownloads),
      fetchJson<StorageLocation[]>("/api/storage").then(setStorage),
      fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates").then(setCandidates),
      fetchJson<RemoteTransfer[]>("/api/transfers").then(setTransfers),
      fetchJson<JobRun[]>("/api/jobs").then(setJobs),
      refreshSettings()
    ]);
  }, [auth?.authenticated, refreshSettings]);

  React.useEffect(() => {
    if (!auth?.authenticated) {
      return;
    }

    const hasActiveTransfers = transfers.some((transfer) => transfer.status === "Queued" || transfer.status === "Running");
    if (activePage !== "Transfers" && !hasActiveTransfers) {
      return;
    }

    const timer = window.setInterval(() => {
      void fetchJson<RemoteTransfer[]>("/api/transfers").then(setTransfers);
    }, 2000);

    return () => window.clearInterval(timer);
  }, [activePage, auth?.authenticated, transfers]);

  React.useEffect(() => {
    const onHashChange = () => {
      const route = getRouteFromHash();
      setActivePage(route.page);
      setConnectionTab(route.connectionTab);
    };
    window.addEventListener("hashchange", onHashChange);
    return () => window.removeEventListener("hashchange", onHashChange);
  }, []);

  if (!auth) {
    return <div className="grid min-h-screen place-items-center bg-[#eef1ec] text-sm text-slate-600">Loading Flowarr...</div>;
  }

  if (!auth.authenticated) {
    return <AuthScreen setupRequired={!auth.setupComplete} onAuthenticated={() => setAuth({ setupComplete: true, authenticated: true })} />;
  }

  const pages = [
    { name: "Dashboard", icon: Activity },
    { name: "Connections", icon: Plug },
    { name: "Storage", icon: HardDrive },
    { name: "Downloads", icon: Download },
    { name: "Transfers", icon: FolderDown },
    { name: "Cleanup", icon: Trash2 },
    { name: "Settings", icon: Settings }
  ];

  return (
    <div className="min-h-screen bg-[#eef1ec] text-ink">
      <aside className="fixed inset-y-0 left-0 hidden w-64 border-r border-line bg-white/85 px-4 py-5 lg:block">
        <div className="mb-8 flex items-center gap-3">
          <div className="grid h-10 w-10 place-items-center rounded bg-accent text-white">
            <ShieldCheck size={22} />
          </div>
          <div>
            <h1 className="text-xl font-semibold">Flowarr</h1>
            <p className="text-xs text-slate-500">Media operations</p>
          </div>
        </div>
        <nav className="space-y-1">
          {pages.map((page) => (
            <button
              key={page.name}
              onClick={() => navigateToPage(page.name, connectionTab)}
              className={`flex w-full items-center gap-3 rounded px-3 py-2 text-left text-sm transition ${
                activePage === page.name ? "bg-accent text-white" : "hover:bg-panel"
              }`}
            >
              <page.icon size={18} />
              {page.name}
            </button>
          ))}
        </nav>
      </aside>

      <main className="lg:pl-64">
        <div className="mx-auto max-w-7xl px-4 py-5 sm:px-6 lg:px-8">
          <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-2xl font-semibold">{activePage}</h2>
              <p className="text-sm text-slate-600">Dry-run cleanup is enabled by default. Deletion requires manual approval.</p>
            </div>
            <button
              onClick={async () => {
                await postJson("/api/auth/logout");
                setAuth({ setupComplete: true, authenticated: false });
              }}
              className="inline-flex items-center gap-2 rounded border border-line bg-white px-3 py-2 text-sm"
            >
              <LogOut size={16} />
              Logout
            </button>
            <div className="flex rounded border border-line bg-white p-1 lg:hidden">
              {pages.map((page) => (
                <button
                  key={page.name}
                  title={page.name}
                  onClick={() => navigateToPage(page.name, connectionTab)}
                  className={`grid h-10 w-10 place-items-center rounded ${activePage === page.name ? "bg-accent text-white" : ""}`}
                >
                  <page.icon size={18} />
                </button>
              ))}
            </div>
          </div>

          {activePage === "Dashboard" && <Dashboard dashboard={dashboard} />}
          {activePage === "Connections" && <Connections settings={connectionSettings} error={settingsError} activeTab={connectionTab} onTabChange={setConnectionTab} onSaved={refreshSettings} />}
          {activePage === "Storage" && <Storage storage={storage} setStorage={setStorage} />}
          {activePage === "Downloads" && <Downloads downloads={downloads} />}
          {activePage === "Transfers" && <Transfers transfers={transfers} settings={transferSettings} />}
          {activePage === "Cleanup" && <Cleanup candidates={candidates} setCandidates={setCandidates} />}
          {activePage === "Settings" && <SettingsPanel transferSettings={transferSettings} jobs={jobs} />}
        </div>
      </main>
    </div>
  );
}

async function fetchJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, { credentials: "include" });
  if (!response.ok) throw new Error(`Request failed: ${path}`);
  return response.json();
}

async function putJson(path: string, body: unknown) {
  const response = await fetch(`${apiBase}${path}`, {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await getErrorMessage(response, path));
}

function normalizeAutoDownload(autoDownload: Partial<ConnectionSettings["autoDownload"]> | undefined): ConnectionSettings["autoDownload"] {
  return {
    enabled: autoDownload?.enabled ?? false,
    localImportPath: autoDownload?.localImportPath ?? "/media/import",
    overwriteExisting: autoDownload?.overwriteExisting ?? false,
    allowedExtensions: autoDownload?.allowedExtensions ?? [".mkv", ".mp4", ".avi", ".mov", ".m4v", ".srt"],
    localMediaPaths: (autoDownload?.localMediaPaths ?? []).map((path) => ({ ...path, id: path.id ?? createClientId() })),
    transferRules: (autoDownload?.transferRules ?? []).map((rule) => ({ ...rule, sourceRootKind: rule.sourceRootKind ?? "Download", sourceRootPath: rule.sourceRootPath ?? "", id: rule.id ?? createClientId() }))
  };
}

function createClientId() {
  return globalThis.crypto?.randomUUID?.() ?? `local-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function AuthScreen({ setupRequired, onAuthenticated }: { setupRequired: boolean; onAuthenticated: () => void }) {
  const [username, setUsername] = React.useState("admin");
  const [password, setPassword] = React.useState("");
  const [message, setMessage] = React.useState("");

  async function submit() {
    setMessage(setupRequired ? "Creating admin..." : "Logging in...");
    try {
      await postJson(setupRequired ? "/api/auth/setup" : "/api/auth/login", { username, password });
      onAuthenticated();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Authentication failed.");
    }
  }

  return (
    <div className="grid min-h-screen place-items-center bg-[#eef1ec] px-4">
      <div className="w-full max-w-sm rounded border border-line bg-white p-5">
        <div className="mb-5 flex items-center gap-3">
          <div className="grid h-10 w-10 place-items-center rounded bg-accent text-white">
            <ShieldCheck size={21} />
          </div>
          <div>
            <h1 className="text-lg font-semibold">Flowarr</h1>
            <p className="text-sm text-slate-600">{setupRequired ? "First-run admin setup" : "Admin login"}</p>
          </div>
        </div>
        <div className="grid gap-3">
          <Input label="Username" value={username} onChange={setUsername} />
          <Input label="Password" type="password" value={password} onChange={setPassword} />
          {setupRequired && <p className="text-xs text-slate-500">Use at least 12 characters. The password is stored as a salted hash.</p>}
          <button onClick={submit} className="rounded bg-accent px-4 py-2 text-sm font-medium text-white">
            {setupRequired ? "Create admin" : "Login"}
          </button>
          {message && <p className="text-sm text-slate-600">{message}</p>}
        </div>
      </div>
    </div>
  );
}

function defaultConnectionSettings(): ConnectionSettings {
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
      remoteRootPath: "",
      seedboxRoots: [],
      hasPassword: false,
      hasPrivateKeyPassphrase: false
    },
    autoDownload: normalizeAutoDownload(undefined),
    radarr: { enabled: false, baseUrl: "", hasApiKey: false },
    sonarr: { enabled: false, baseUrl: "", hasApiKey: false }
  };
}

function normalizeConnectionSettings(settings: RawConnectionSettings): ConnectionSettings {
  const defaults = defaultConnectionSettings();
  return {
    qbittorrent: settings.qbittorrent ?? settings.qBittorrent ?? defaults.qbittorrent,
    sftpSeedbox: settings.sftpSeedbox ?? settings.SftpSeedbox ?? settings.sftpseedbox ?? defaults.sftpSeedbox,
    autoDownload: normalizeAutoDownload(settings.autoDownload),
    radarr: settings.radarr ?? defaults.radarr,
    sonarr: settings.sonarr ?? defaults.sonarr
  };
}

async function postJson<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    method: "POST",
    credentials: "include",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await getErrorMessage(response, path));
  return response.json();
}

async function getErrorMessage(response: Response, path: string) {
  const body = await response.text();
  if (!body) return `Request failed: ${path} (${response.status})`;

  try {
    const parsed = JSON.parse(body) as { message?: string; title?: string; errors?: Record<string, string[]> };
    if (parsed.message) return parsed.message;
    if (parsed.errors) return Object.entries(parsed.errors).map(([field, values]) => `${field}: ${values.join(", ")}`).join(" ");
    if (parsed.title) return parsed.title;
  } catch {
    return body;
  }

  return `Request failed: ${path} (${response.status})`;
}

function Dashboard({ dashboard }: { dashboard: DashboardSummary | null }) {
  return (
    <section className="space-y-5">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Metric title="Local used" value={formatBytes(dashboard?.localStorage.usedBytes ?? 0)} sub={`${formatBytes(dashboard?.localStorage.freeBytes ?? 0)} free`} />
        <Metric title="Seedbox used" value={formatBytes(dashboard?.seedboxStorage.usedBytes ?? 0)} sub={`${formatBytes(dashboard?.seedboxStorage.freeBytes ?? 0)} free`} />
        <Metric title="Active downloads" value={String(dashboard?.activeDownloads ?? 0)} sub="Torrent client sync status" />
        <Metric title="Cleanup candidates" value={String(dashboard?.cleanupCandidates.length ?? 0)} sub="Manual review required" />
      </div>
      <TwoColumn
        leftTitle="Storage paths"
        leftItems={(dashboard?.storageLocations ?? []).map((item) => `${item.name}: ${formatBytes(item.usedBytes)} used, ${formatBytes(item.freeBytes)} free at ${item.pathOrEndpoint}`)}
        rightTitle="Torrent client"
        rightItems={(dashboard?.downloads ?? []).map((item) => `${item.name} - ${item.status} (${Math.round(item.progress * 100)}%)`)}
      />
      <TwoColumn
        leftTitle="Recent imports"
        leftItems={(dashboard?.recentImports ?? []).map((item) => `${item.title} imported ${new Date(item.importedAt).toLocaleString()}`)}
        rightTitle="Cleanup review"
        rightItems={(dashboard?.cleanupCandidates ?? []).map((item) => `${item.path} - ${item.confidence}`)}
      />
    </section>
  );
}

function Connections({
  settings,
  error,
  activeTab,
  onTabChange,
  onSaved
}: {
  settings: ConnectionSettings | null;
  error: string;
  activeTab: string;
  onTabChange: (tab: string) => void;
  onSaved: () => Promise<void>;
}) {
  if (!settings) {
    return (
      <div className="rounded border border-line bg-white p-4">
        <p className="text-sm text-slate-600">Loading connection settings...</p>
        {error && <p className="mt-2 text-sm text-danger">{error}</p>}
      </div>
    );
  }

  return (
    <section className="space-y-4">
      <ConnectionForms settings={settings} activeTab={activeTab} onTabChange={onTabChange} onSaved={onSaved} />
    </section>
  );
}

function ConnectionForms({ settings, activeTab, onTabChange, onSaved }: { settings: ConnectionSettings; activeTab: string; onTabChange: (tab: string) => void; onSaved: () => Promise<void> }) {
  const [qbittorrent, setQbittorrent] = React.useState({ ...settings.qbittorrent, password: "", clearPassword: false });
  const [sftpSeedbox, setSftpSeedbox] = React.useState({ ...settings.sftpSeedbox, seedboxRoots: settings.sftpSeedbox.seedboxRoots.map((root) => ({ ...root, id: root.id ?? createClientId() })), password: "", clearPassword: false, privateKeyPassphrase: "", clearPrivateKeyPassphrase: false });
  const [autoDownload, setAutoDownload] = React.useState({ ...settings.autoDownload, allowedExtensionsText: settings.autoDownload.allowedExtensions.join(", ") });
  const [radarr, setRadarr] = React.useState({ ...settings.radarr, apiKey: "", clearApiKey: false });
  const [sonarr, setSonarr] = React.useState({ ...settings.sonarr, apiKey: "", clearApiKey: false });
  const [message, setMessage] = React.useState("");
  const [torrentTest, setTorrentTest] = React.useState<ConnectionTestResult | null>(null);
  const [sftpTest, setSftpTest] = React.useState<ConnectionTestResult | null>(null);
  const [radarrTest, setRadarrTest] = React.useState<ConnectionTestResult | null>(null);
  const [sonarrTest, setSonarrTest] = React.useState<ConnectionTestResult | null>(null);
  const [testingTorrent, setTestingTorrent] = React.useState(false);
  const [testingSftp, setTestingSftp] = React.useState(false);
  const enabledSeedboxRoots = sftpSeedbox.seedboxRoots.filter((root) => root.enabled && root.path.trim());
  const defaultSourceRoot = enabledSeedboxRoots.find((root) => root.kind === "Download") ?? enabledSeedboxRoots[0];

  React.useEffect(() => {
    setQbittorrent({ ...settings.qbittorrent, password: "", clearPassword: false });
    setSftpSeedbox({ ...settings.sftpSeedbox, seedboxRoots: settings.sftpSeedbox.seedboxRoots.map((root) => ({ ...root, id: root.id ?? createClientId() })), password: "", clearPassword: false, privateKeyPassphrase: "", clearPrivateKeyPassphrase: false });
    setAutoDownload({ ...settings.autoDownload, allowedExtensionsText: settings.autoDownload.allowedExtensions.join(", ") });
    setRadarr({ ...settings.radarr, apiKey: "", clearApiKey: false });
    setSonarr({ ...settings.sonarr, apiKey: "", clearApiKey: false });
    setTorrentTest(null);
    setSftpTest(null);
    setRadarrTest(null);
    setSonarrTest(null);
  }, [settings]);

  async function saveTorrentClient() {
    setMessage("Saving torrent client...");
    await putJson("/api/connections/qbittorrent", {
      ...qbittorrent,
      clientType: qbittorrent.clientType || "Deluge",
      baseUrl: qbittorrent.baseUrl || "",
      username: qbittorrent.clientType === "Deluge" ? "" : qbittorrent.username || ""
    });
    await onSaved();
    setMessage("Torrent client saved securely.");
  }

  async function testTorrentClient() {
    setMessage("Testing torrent client...");
    setTestingTorrent(true);
    setTorrentTest({ ok: true, message: "Testing torrent client..." });
    try {
      const result = await postJson<ConnectionTestResult>("/api/connections/qbittorrent/test", {
        ...qbittorrent,
        clientType: qbittorrent.clientType || "Deluge",
        baseUrl: qbittorrent.baseUrl || "",
        username: qbittorrent.clientType === "Deluge" ? "" : qbittorrent.username || ""
      });
      setTorrentTest(result);
      setMessage(`${result.ok ? "Torrent test passed" : "Torrent test failed"}: ${result.message}`);
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : "Torrent test failed.";
      setTorrentTest({ ok: false, message: errorMessage });
      setMessage(errorMessage);
    } finally {
      setTestingTorrent(false);
    }
  }

  async function saveSeedbox() {
    setMessage("Saving seedbox...");
    await putJson("/api/connections/sftp-seedbox", {
      ...sftpSeedbox,
      host: sftpSeedbox.host || "",
      port: Number(sftpSeedbox.port) || 22,
      username: sftpSeedbox.username || "",
      privateKeyPath: sftpSeedbox.privateKeyPath || "",
      remoteRootPath: sftpSeedbox.remoteRootPath || "",
      seedboxRoots: sftpSeedbox.seedboxRoots.map(({ id, ...root }) => root)
    });
    await onSaved();
    setMessage("Seedbox saved securely.");
  }

  async function testSeedbox() {
    setMessage("Testing SFTP seedbox...");
    setTestingSftp(true);
    setSftpTest({ ok: true, message: "Testing SFTP seedbox..." });
    try {
      const result = await postJson<ConnectionTestResult>("/api/connections/sftp-seedbox/test", {
        ...sftpSeedbox,
        host: sftpSeedbox.host || "",
        port: Number(sftpSeedbox.port) || 22,
        username: sftpSeedbox.username || "",
        privateKeyPath: sftpSeedbox.privateKeyPath || "",
        remoteRootPath: sftpSeedbox.remoteRootPath || "",
        seedboxRoots: sftpSeedbox.seedboxRoots.map(({ id, ...root }) => root)
      });
      setSftpTest(result);
      setMessage(`${result.ok ? "SFTP test passed" : "SFTP test failed"}: ${result.message}`);
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : "SFTP test failed.";
      setSftpTest({ ok: false, message: errorMessage });
      setMessage(errorMessage);
    } finally {
      setTestingSftp(false);
    }
  }

  async function saveAutoDownload() {
    setMessage("Saving auto-download...");
    await putJson("/api/connections/auto-download", {
      ...autoDownload,
      allowedExtensions: autoDownload.allowedExtensionsText.split(",").map((item) => item.trim()).filter(Boolean),
      localMediaPaths: autoDownload.localMediaPaths.map((path) => ({
        enabled: path.enabled,
        name: path.name || "Media path",
        path: path.path || "/media",
        role: path.role || "Mixed"
      })),
      transferRules: autoDownload.transferRules.map((rule) => ({
        enabled: rule.enabled,
        name: rule.name || "Rule",
        remotePathPrefix: rule.remotePathPrefix || "",
        localPath: rule.localPath || autoDownload.localImportPath || "/media/import",
        sourceRootKind: rule.sourceRootKind || "Download",
        sourceRootPath: rule.sourceRootPath || ""
      }))
    });
    await onSaved();
    setMessage("Auto-download saved.");
  }

  async function saveArr(system: "radarr" | "sonarr") {
    const state = system === "radarr" ? radarr : sonarr;
    setMessage(`Saving ${system}...`);
    await putJson(`/api/connections/${system}`, state);
    await onSaved();
    setMessage(`${system} saved securely.`);
  }

  async function testArr(system: "radarr" | "sonarr") {
    const state = system === "radarr" ? radarr : sonarr;
    const setter = system === "radarr" ? setRadarrTest : setSonarrTest;
    setter({ ok: true, message: `Testing ${system}...` });
    const result = await postJson<ConnectionTestResult>(`/api/connections/${system}/test`, state);
    setter(result);
  }

  return (
    <div className="space-y-4">
      <ConnectionTabs
        activeTab={activeTab}
        onChange={(tab) => {
          onTabChange(tab);
          navigateToConnectionTab(tab);
        }}
        tabs={[
          { id: "torrent", label: "Torrent Client" },
          { id: "sftp", label: "SFTP" },
          { id: "services", label: "Services" },
          { id: "plex", label: "Plex" },
          { id: "jellyfin", label: "Jellyfin" }
        ]}
      />

      {activeTab === "torrent" && (
        <div className="rounded border border-line bg-white p-4">
          <CardTitleWithInfo
            title="Torrent client"
            description="Your download client, such as Deluge or qBittorrent. Flowarr reads torrent status and remote paths from here."
          />
          <div className="mt-4 grid gap-3">
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={qbittorrent.enabled} onChange={(event) => setQbittorrent({ ...qbittorrent, enabled: event.target.checked })} />
              Enabled
            </label>
            <label className="grid gap-1 text-sm">
              <span className="text-slate-600">Client</span>
              <select
                value={qbittorrent.clientType || "qBittorrent"}
                onChange={(event) => setQbittorrent({ ...qbittorrent, clientType: event.target.value })}
                className="rounded border border-line px-3 py-2 text-ink outline-none focus:border-accent"
              >
                <option value="Deluge">Deluge</option>
                <option value="qBittorrent">qBittorrent</option>
              </select>
            </label>
            <Input label="Base URL" value={qbittorrent.baseUrl} onChange={(value) => setQbittorrent({ ...qbittorrent, baseUrl: value })} placeholder={qbittorrent.clientType === "Deluge" ? "https://your-deluge-web-host" : "https://your-qbit-host"} />
            {qbittorrent.clientType !== "Deluge" && (
              <Input label="Username" value={qbittorrent.username} onChange={(value) => setQbittorrent({ ...qbittorrent, username: value })} />
            )}
            <Input label={qbittorrent.hasPassword ? "Password (already saved)" : "Password"} type="password" value={qbittorrent.password} onChange={(value) => setQbittorrent({ ...qbittorrent, password: value })} placeholder={qbittorrent.hasPassword ? "Leave blank to keep current" : ""} />
            <p className="text-xs text-slate-500">Leave blank to use the saved encrypted password. Typed passwords are sent once to the Flowarr API for save/test.</p>
            {qbittorrent.hasPassword && (
              <label className="flex items-center gap-2 text-sm text-slate-600">
                <input type="checkbox" checked={qbittorrent.clearPassword} onChange={(event) => setQbittorrent({ ...qbittorrent, clearPassword: event.target.checked })} />
                Clear saved password
              </label>
            )}
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={saveTorrentClient} className="rounded bg-accent px-4 py-2 text-sm font-medium text-white">Save torrent client</button>
              <button type="button" onClick={testTorrentClient} disabled={testingTorrent} className="rounded border border-line px-4 py-2 text-sm font-medium disabled:cursor-wait disabled:opacity-60">
                {testingTorrent ? "Testing..." : "Test connection"}
              </button>
            </div>
            {torrentTest && <ConnectionTestStatus result={torrentTest} pending={testingTorrent} />}
          </div>
        </div>
      )}

      {activeTab === "sftp" && (
        <>
        <div className="rounded border border-line bg-white p-4">
          <CardTitleWithInfo
            title="SFTP seedbox"
            description="SFTP access to your seedbox files. Flowarr uses this for browsing seedbox roots, scanning storage, and downloading completed files."
          />
          <div className="mt-4 grid gap-3">
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={sftpSeedbox.enabled} onChange={(event) => setSftpSeedbox({ ...sftpSeedbox, enabled: event.target.checked })} />
              Enabled
            </label>
            <Input label="Host" value={sftpSeedbox.host} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, host: value })} placeholder="your-ultra-sftp-host" />
            <Input label="Port" value={String(sftpSeedbox.port)} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, port: Number(value) || 22 })} />
            <Input label="Username" value={sftpSeedbox.username} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, username: value })} />
            <Input label={sftpSeedbox.hasPassword ? "Password (already saved)" : "Password"} type="password" value={sftpSeedbox.password} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, password: value })} placeholder={sftpSeedbox.hasPassword ? "Leave blank to keep current" : ""} />
            <p className="text-xs text-slate-500">Leave blank to use the saved encrypted password. Typed passwords are sent once to the Flowarr API for save/test.</p>
            <Input label="Private key path" value={sftpSeedbox.privateKeyPath} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, privateKeyPath: value })} placeholder="/keys/id_rsa" />
            <Input label={sftpSeedbox.hasPrivateKeyPassphrase ? "Key passphrase (already saved)" : "Key passphrase"} type="password" value={sftpSeedbox.privateKeyPassphrase} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, privateKeyPassphrase: value })} />
            <Input label="Legacy remote root path" value={sftpSeedbox.remoteRootPath} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, remoteRootPath: value })} placeholder="/home/user/files" />
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <h4 className="text-sm font-medium">Seedbox roots</h4>
                <button
                  type="button"
                  onClick={() => setSftpSeedbox({
                    ...sftpSeedbox,
                    seedboxRoots: [...sftpSeedbox.seedboxRoots, { id: createClientId(), enabled: true, name: "Downloads", kind: "Download", path: "/downloads" }]
                  })}
                  className="rounded border border-line px-3 py-1 text-sm"
                >
                  Add root
                </button>
              </div>
              {sftpSeedbox.seedboxRoots.map((root, index) => (
                <div key={root.id ?? index} className="grid gap-2 rounded border border-line bg-panel p-3 md:grid-cols-[auto_1fr_1fr_2fr_auto]">
                  <label className="flex items-center gap-2 text-sm">
                    <input type="checkbox" checked={root.enabled} onChange={(event) => updateSeedboxRoot(index, { enabled: event.target.checked })} />
                    Enabled
                  </label>
                  <Input label="Name" value={root.name} onChange={(value) => updateSeedboxRoot(index, { name: value })} placeholder="Movies" />
                  <label className="grid gap-1 text-sm">
                    <span className="text-slate-600">Kind</span>
                    <select value={root.kind} onChange={(event) => updateSeedboxRoot(index, { kind: event.target.value as "Download" | "Media" })} className="rounded border border-line px-3 py-2">
                      <option value="Download">Download</option>
                      <option value="Media">Media</option>
                    </select>
                  </label>
                  <Input label="Remote path" value={root.path} onChange={(value) => updateSeedboxRoot(index, { path: value })} placeholder="/media/Movies" />
                  <button type="button" onClick={() => setSftpSeedbox({ ...sftpSeedbox, seedboxRoots: sftpSeedbox.seedboxRoots.filter((_, rootIndex) => rootIndex !== index) })} className="self-end rounded border border-line px-3 py-2 text-sm">Remove</button>
                </div>
              ))}
            </div>
            <div className="grid gap-2 text-sm text-slate-600">
              {sftpSeedbox.hasPassword && (
                <label className="flex items-center gap-2">
                  <input type="checkbox" checked={sftpSeedbox.clearPassword} onChange={(event) => setSftpSeedbox({ ...sftpSeedbox, clearPassword: event.target.checked })} />
                  Clear saved password
                </label>
              )}
              {sftpSeedbox.hasPrivateKeyPassphrase && (
                <label className="flex items-center gap-2">
                  <input type="checkbox" checked={sftpSeedbox.clearPrivateKeyPassphrase} onChange={(event) => setSftpSeedbox({ ...sftpSeedbox, clearPrivateKeyPassphrase: event.target.checked })} />
                  Clear saved key passphrase
                </label>
              )}
            </div>
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={saveSeedbox} className="rounded bg-accent px-4 py-2 text-sm font-medium text-white">Save seedbox</button>
              <button type="button" onClick={testSeedbox} disabled={testingSftp} className="rounded border border-line px-4 py-2 text-sm font-medium disabled:cursor-wait disabled:opacity-60">
                {testingSftp ? "Testing..." : "Test connection"}
              </button>
            </div>
            {sftpTest && <ConnectionTestStatus result={sftpTest} pending={testingSftp} />}
          </div>
        </div>
        </>
      )}

      {activeTab === "services" && (
      <div className="grid gap-4 xl:grid-cols-2">
        <ArrConnectionCard title="Radarr" value={radarr} test={radarrTest} onChange={setRadarr} onSave={() => saveArr("radarr")} onTest={() => testArr("radarr")} />
        <ArrConnectionCard title="Sonarr" value={sonarr} test={sonarrTest} onChange={setSonarr} onSave={() => saveArr("sonarr")} onTest={() => testArr("sonarr")} />
      </div>
      )}

      {activeTab === "sftp" && (
      <div className="rounded border border-line bg-white p-4">
        <h3 className="font-medium">Auto download</h3>
        <div className="mt-4 grid gap-3 md:grid-cols-2">
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={autoDownload.enabled} onChange={(event) => setAutoDownload({ ...autoDownload, enabled: event.target.checked })} />
            Enabled
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={autoDownload.overwriteExisting} onChange={(event) => setAutoDownload({ ...autoDownload, overwriteExisting: event.target.checked })} />
            Overwrite existing files
          </label>
          <LocalPathInput label="Legacy fallback destination" value={autoDownload.localImportPath} onChange={(value) => setAutoDownload({ ...autoDownload, localImportPath: value })} placeholder="/media/import" />
          <Input label="Allowed extensions" value={autoDownload.allowedExtensionsText} onChange={(value) => setAutoDownload({ ...autoDownload, allowedExtensionsText: value })} placeholder=".mkv, .mp4, .srt" />
        </div>
        <div className="mt-5 space-y-3">
          <div className="flex items-center justify-between gap-3">
            <SectionTitleWithInfo
              title="Local media paths"
              description="Local library folders inside the container, such as /media/Movies or /media/TV Shows. Flowarr scans these as your Plex/Jellyfin media destinations."
            />
            <button
              onClick={() => setAutoDownload({
                ...autoDownload,
                localMediaPaths: [...autoDownload.localMediaPaths, { id: createClientId(), enabled: true, name: "New path", path: "/media", role: "Mixed" }]
              })}
              className="rounded border border-line px-3 py-1 text-sm"
            >
              Add path
            </button>
          </div>
          {autoDownload.localMediaPaths.map((path, index) => (
            <div key={path.id ?? index} className="grid gap-3 rounded border border-line bg-panel p-3 md:grid-cols-[auto_1fr_1fr_1fr_auto]">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={path.enabled}
                  onChange={(event) => updateLocalMediaPath(index, { enabled: event.target.checked })}
                />
                Enabled
              </label>
              <Input label="Name" value={path.name} onChange={(value) => updateLocalMediaPath(index, { name: value })} placeholder="Movies" />
              <LocalPathInput label="Container path" value={path.path} onChange={(value) => updateLocalMediaPath(index, { path: value })} placeholder="/media/Movies" />
              <Input label="Role" value={path.role} onChange={(value) => updateLocalMediaPath(index, { role: value })} placeholder="Movies" />
              <button
                onClick={() => setAutoDownload({ ...autoDownload, localMediaPaths: autoDownload.localMediaPaths.filter((_, pathIndex) => pathIndex !== index) })}
                className="self-end rounded border border-line px-3 py-2 text-sm"
              >
                Remove
              </button>
            </div>
          ))}
        </div>
        <div className="mt-5 space-y-3">
          <div className="flex items-center justify-between gap-3">
            <SectionTitleWithInfo
              title="Download folder rules"
              description="Rules that map a seedbox source folder to a local media path. Use one rule per flow, for example Movies downloads to /media/Movies and TV downloads to /media/TV Shows."
            />
            <button
              onClick={() => setAutoDownload({
                ...autoDownload,
                transferRules: [...autoDownload.transferRules, { id: createClientId(), enabled: true, name: "New rule", remotePathPrefix: "", localPath: autoDownload.localMediaPaths[0]?.path || autoDownload.localImportPath || "/media/import", sourceRootKind: defaultSourceRoot?.kind ?? "Download", sourceRootPath: defaultSourceRoot?.path ?? "" }]
              })}
              className="rounded border border-line px-3 py-1 text-sm"
            >
              Add rule
            </button>
          </div>
          {autoDownload.transferRules.map((rule, index) => (
            <div key={rule.id ?? index} className="grid gap-3 rounded border border-line bg-panel p-3 md:grid-cols-[auto_1fr_1fr_1fr_auto]">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={rule.enabled}
                  onChange={(event) => updateTransferRule(index, { enabled: event.target.checked })}
                />
                Enabled
              </label>
              <Input label="Name" value={rule.name} onChange={(value) => updateTransferRule(index, { name: value })} placeholder="Movies" />
              <SeedboxRootSelect
                roots={enabledSeedboxRoots}
                rule={rule}
                onChange={(root) => updateTransferRule(index, { sourceRootKind: root?.kind ?? "Download", sourceRootPath: root?.path ?? "" })}
              />
              <SeedboxPathInput
                label="Remote folder"
                value={rule.remotePathPrefix}
                sourceRootKind={rule.sourceRootKind}
                sourceRootPath={rule.sourceRootPath}
                onChange={(value) => updateTransferRule(index, { remotePathPrefix: value })}
                placeholder={rule.sourceRootPath ? "optional subfolder" : "/downloads/movies"}
              />
              <LocalPathInput label="Local destination" value={rule.localPath} onChange={(value) => updateTransferRule(index, { localPath: value })} placeholder="/media/Movies" />
              <button
                onClick={() => setAutoDownload({ ...autoDownload, transferRules: autoDownload.transferRules.filter((_, ruleIndex) => ruleIndex !== index) })}
                className="self-end rounded border border-line px-3 py-2 text-sm"
              >
                Remove
              </button>
            </div>
          ))}
        </div>
        <button onClick={saveAutoDownload} className="mt-4 rounded bg-accent px-4 py-2 text-sm font-medium text-white">Save auto-download</button>
      </div>
      )}

      {activeTab === "plex" && <PlannedServicePanel name="Plex" />}
      {activeTab === "jellyfin" && <PlannedServicePanel name="Jellyfin" />}

      <div className="flex items-center gap-3">
        <p className="text-sm text-slate-600">{message}</p>
      </div>
    </div>
  );

  function updateTransferRule(index: number, patch: Partial<TransferRule>) {
    setAutoDownload({
      ...autoDownload,
      transferRules: autoDownload.transferRules.map((rule, ruleIndex) =>
        ruleIndex === index ? { ...rule, ...patch } : rule)
    });
  }

  function updateLocalMediaPath(index: number, patch: Partial<LocalMediaPath>) {
    setAutoDownload({
      ...autoDownload,
      localMediaPaths: autoDownload.localMediaPaths.map((path, pathIndex) =>
        pathIndex === index ? { ...path, ...patch } : path)
    });
  }

  function updateSeedboxRoot(index: number, patch: Partial<SeedboxRoot>) {
    setSftpSeedbox({
      ...sftpSeedbox,
      seedboxRoots: sftpSeedbox.seedboxRoots.map((root, rootIndex) =>
        rootIndex === index ? { ...root, ...patch } : root)
    });
  }
}

function ArrConnectionCard({
  title,
  value,
  test,
  onChange,
  onSave,
  onTest
}: {
  title: string;
  value: ArrSettings & { apiKey: string; clearApiKey: boolean };
  test: ConnectionTestResult | null;
  onChange: (value: ArrSettings & { apiKey: string; clearApiKey: boolean }) => void;
  onSave: () => Promise<void>;
  onTest: () => Promise<void>;
}) {
  return (
    <div className="rounded border border-line bg-white p-4">
      <h3 className="font-medium">{title}</h3>
      <div className="mt-4 grid gap-3">
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={value.enabled} onChange={(event) => onChange({ ...value, enabled: event.target.checked })} />
          Enabled
        </label>
        <Input label="Base URL" value={value.baseUrl} onChange={(baseUrl) => onChange({ ...value, baseUrl })} placeholder={`https://your-${title.toLowerCase()}-host`} />
        <Input label={value.hasApiKey ? "API key (already saved)" : "API key"} type="password" value={value.apiKey} onChange={(apiKey) => onChange({ ...value, apiKey })} placeholder={value.hasApiKey ? "Leave blank to keep current" : ""} />
        {value.hasApiKey && (
          <label className="flex items-center gap-2 text-sm text-slate-600">
            <input type="checkbox" checked={value.clearApiKey} onChange={(event) => onChange({ ...value, clearApiKey: event.target.checked })} />
            Clear saved API key
          </label>
        )}
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={onSave} className="rounded bg-accent px-4 py-2 text-sm font-medium text-white">Save {title}</button>
          <button type="button" onClick={onTest} className="rounded border border-line px-4 py-2 text-sm font-medium">Test connection</button>
        </div>
        {test && <ConnectionTestStatus result={test} pending={test.message.startsWith("Testing")} />}
      </div>
    </div>
  );
}

function ConnectionTestStatus({ result, pending }: { result: ConnectionTestResult; pending: boolean }) {
  const styles = pending
    ? "border-blue-200 bg-blue-50 text-blue-800"
    : result.ok
      ? "border-emerald-200 bg-emerald-50 text-accent"
      : "border-rose-200 bg-rose-50 text-danger";

  return (
    <div className={`rounded border px-3 py-2 text-sm ${styles}`}>
      {result.message}
    </div>
  );
}

function ConnectionTabs({ activeTab, tabs, onChange }: { activeTab: string; tabs: { id: string; label: string }[]; onChange: (tab: string) => void }) {
  return (
    <div className="overflow-x-auto rounded border border-line bg-white p-1">
      <div className="flex min-w-max gap-1">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            type="button"
            onClick={() => onChange(tab.id)}
            className={`rounded px-4 py-2 text-sm font-medium ${activeTab === tab.id ? "bg-accent text-white" : "text-slate-700 hover:bg-panel"}`}
          >
            {tab.label}
          </button>
        ))}
      </div>
    </div>
  );
}

function PlannedServicePanel({ name }: { name: string }) {
  return (
    <div className="rounded border border-line bg-white p-4">
      <CardTitleWithInfo
        title={name}
        description={`${name} integration is planned. Flowarr will use it later for library awareness and playback-server status.`}
      />
      <p className="mt-3 text-sm text-slate-600">Not configured yet.</p>
    </div>
  );
}

function Input({ label, value, onChange, placeholder, type = "text" }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string; type?: string }) {
  return (
    <label className="grid gap-1 text-sm">
      <span className="text-slate-600">{label}</span>
      <input
        type={type}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        className="rounded border border-line px-3 py-2 text-ink outline-none focus:border-accent"
      />
    </label>
  );
}

function SectionTitleWithInfo({ title, description }: { title: string; description: string }) {
  return (
    <div className="flex items-center gap-2">
      <h4 className="text-sm font-medium">{title}</h4>
      <span className="group relative inline-flex">
        <Info size={15} className="text-slate-500" />
        <span className="pointer-events-none absolute left-1/2 top-6 z-20 hidden w-72 -translate-x-1/2 rounded border border-line bg-white px-3 py-2 text-xs font-normal text-slate-700 shadow-lg group-hover:block">
          {description}
        </span>
      </span>
    </div>
  );
}

function CardTitleWithInfo({ title, description }: { title: string; description: string }) {
  return (
    <div className="flex items-center gap-2">
      <h3 className="font-medium">{title}</h3>
      <span className="group relative inline-flex">
        <Info size={15} className="text-slate-500" />
        <span className="pointer-events-none absolute left-1/2 top-6 z-20 hidden w-72 -translate-x-1/2 rounded border border-line bg-white px-3 py-2 text-xs font-normal text-slate-700 shadow-lg group-hover:block">
          {description}
        </span>
      </span>
    </div>
  );
}

function SeedboxRootSelect({ roots, rule, onChange }: { roots: SeedboxRoot[]; rule: TransferRule; onChange: (root: SeedboxRoot | null) => void }) {
  return (
    <label className="grid gap-1 text-sm">
      <span className="text-slate-600">Seedbox source root</span>
      <select
        value={rule.sourceRootPath}
        onChange={(event) => onChange(roots.find((root) => root.path === event.target.value) ?? null)}
        className="rounded border border-line px-3 py-2"
      >
        <option value="">Any {rule.sourceRootKind || "Download"} root</option>
        {roots.map((root) => (
          <option key={`${root.kind}-${root.path}`} value={root.path}>
            {root.name} ({root.kind}) - {root.path}
          </option>
        ))}
      </select>
    </label>
  );
}

function SeedboxPathInput({
  label,
  value,
  sourceRootKind,
  sourceRootPath,
  onChange,
  placeholder
}: {
  label: string;
  value: string;
  sourceRootKind: "Download" | "Media";
  sourceRootPath: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  const [open, setOpen] = React.useState(false);

  return (
    <div className="grid gap-1 text-sm">
      <span className="text-slate-600">{label}</span>
      <div className="flex gap-2">
        <input
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          className="min-w-0 flex-1 rounded border border-line px-3 py-2 text-ink outline-none focus:border-accent"
        />
        <button
          type="button"
          onClick={() => setOpen(true)}
          title={`Browse ${label}`}
          className="grid h-10 w-10 flex-none place-items-center rounded border border-line bg-white text-slate-700 hover:bg-panel"
        >
          <FolderOpen size={18} />
        </button>
      </div>
      {open && (
        <SeedboxFolderBrowser
          initialPath={toAbsoluteSeedboxPath(sourceRootPath, value)}
          sourceRootKind={sourceRootKind}
          sourceRootPath={sourceRootPath}
          onClose={() => setOpen(false)}
          onSelect={(path) => {
            onChange(toRuleRemotePath(sourceRootPath, path));
            setOpen(false);
          }}
        />
      )}
    </div>
  );
}

function LocalPathInput({ label, value, onChange, placeholder }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string }) {
  const [open, setOpen] = React.useState(false);

  return (
    <div className="grid gap-1 text-sm">
      <span className="text-slate-600">{label}</span>
      <div className="flex gap-2">
        <input
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          className="min-w-0 flex-1 rounded border border-line px-3 py-2 text-ink outline-none focus:border-accent"
        />
        <button
          type="button"
          onClick={() => setOpen(true)}
          title={`Browse ${label}`}
          className="grid h-10 w-10 flex-none place-items-center rounded border border-line bg-white text-slate-700 hover:bg-panel"
        >
          <FolderOpen size={18} />
        </button>
      </div>
      {open && (
        <LocalFolderBrowser
          initialPath={value || placeholder || "/media"}
          onClose={() => setOpen(false)}
          onSelect={(path) => {
            onChange(path);
            setOpen(false);
          }}
        />
      )}
    </div>
  );
}

function LocalFolderBrowser({ initialPath, onSelect, onClose }: { initialPath: string; onSelect: (path: string) => void; onClose: () => void }) {
  const [listing, setListing] = React.useState<LocalFolderListing | null>(null);
  const [currentPath, setCurrentPath] = React.useState(initialPath);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState("");

  const loadPath = React.useCallback(async (path: string) => {
    setLoading(true);
    setError("");
    try {
      const query = path ? `?path=${encodeURIComponent(path)}` : "";
      const nextListing = await fetchJson<LocalFolderListing>(`/api/local-folders${query}`);
      setListing(nextListing);
      setCurrentPath(nextListing.currentPath || "");
    } catch (err) {
      setListing(null);
      setError(err instanceof Error ? err.message : "Unable to browse folder.");
    } finally {
      setLoading(false);
    }
  }, []);

  React.useEffect(() => {
    void loadPath(initialPath);
  }, [initialPath, loadPath]);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/40 px-4 py-6">
      <div className="w-full max-w-2xl overflow-hidden rounded border border-line bg-white shadow-xl">
        <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-3">
          <div className="min-w-0">
            <h3 className="font-medium">Browse local folders</h3>
            <p className="truncate text-xs text-slate-500">{listing?.currentPath || "Available roots"}</p>
          </div>
          <button type="button" onClick={onClose} title="Close" className="grid h-9 w-9 place-items-center rounded border border-line">
            <X size={17} />
          </button>
        </div>
        <div className="max-h-[60vh] overflow-y-auto p-4">
          {error && (
            <div className="mb-3 rounded border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-warn">
              {error}
            </div>
          )}
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <button type="button" onClick={() => void loadPath("")} className="rounded border border-line px-3 py-2 text-sm">Roots</button>
            {listing?.parentPath && (
              <button type="button" onClick={() => void loadPath(listing.parentPath!)} className="inline-flex items-center gap-2 rounded border border-line px-3 py-2 text-sm">
                <ChevronUp size={16} />
                Up
              </button>
            )}
            {currentPath && (
              <button type="button" onClick={() => onSelect(currentPath)} className="inline-flex items-center gap-2 rounded bg-accent px-3 py-2 text-sm font-medium text-white">
                <Check size={16} />
                Select
              </button>
            )}
          </div>
          {loading ? (
            <p className="text-sm text-slate-600">Loading folders...</p>
          ) : (
            <div className="space-y-2">
              {(listing?.entries ?? []).length ? (listing?.entries ?? []).map((entry) => (
                <button
                  key={entry.path}
                  type="button"
                  onClick={() => void loadPath(entry.path)}
                  className="flex w-full items-center gap-3 rounded border border-line px-3 py-2 text-left text-sm hover:bg-panel"
                >
                  <FolderOpen size={17} className="text-accent" />
                  <span className="min-w-0 flex-1 truncate">{entry.name}</span>
                  <span className="hidden max-w-xs truncate text-xs text-slate-500 sm:block">{entry.path}</span>
                </button>
              )) : (
                <p className="rounded bg-panel px-3 py-2 text-sm text-slate-600">No folders found here.</p>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function SeedboxFolderBrowser({
  initialPath,
  sourceRootKind,
  sourceRootPath,
  onSelect,
  onClose
}: {
  initialPath: string;
  sourceRootKind: "Download" | "Media";
  sourceRootPath: string;
  onSelect: (path: string) => void;
  onClose: () => void;
}) {
  const [listing, setListing] = React.useState<LocalFolderListing | null>(null);
  const [currentPath, setCurrentPath] = React.useState(initialPath);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState("");

  const loadPath = React.useCallback(async (path: string) => {
    setLoading(true);
    setError("");
    try {
      const query = new URLSearchParams({ kind: sourceRootKind });
      if (sourceRootPath) query.set("rootPath", sourceRootPath);
      if (path) query.set("path", path);
      const nextListing = await fetchJson<LocalFolderListing>(`/api/seedbox-folders?${query.toString()}`);
      setListing(nextListing);
      setCurrentPath(nextListing.currentPath || "");
    } catch (err) {
      setListing(null);
      setError(err instanceof Error ? err.message : "Unable to browse seedbox.");
    } finally {
      setLoading(false);
    }
  }, [sourceRootKind, sourceRootPath]);

  React.useEffect(() => {
    void loadPath(initialPath);
  }, [initialPath, loadPath]);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/40 px-4 py-6">
      <div className="w-full max-w-2xl overflow-hidden rounded border border-line bg-white shadow-xl">
        <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-3">
          <div className="min-w-0">
            <h3 className="font-medium">Browse seedbox folders</h3>
            <p className="truncate text-xs text-slate-500">{listing?.currentPath || sourceRootPath || `${sourceRootKind} roots`}</p>
          </div>
          <button type="button" onClick={onClose} title="Close" className="grid h-9 w-9 place-items-center rounded border border-line">
            <X size={17} />
          </button>
        </div>
        <div className="max-h-[60vh] overflow-y-auto p-4">
          {error && <div className="mb-3 rounded border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-warn">{error}</div>}
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <button type="button" onClick={() => void loadPath("")} className="rounded border border-line px-3 py-2 text-sm">Roots</button>
            {listing?.parentPath && (
              <button type="button" onClick={() => void loadPath(listing.parentPath!)} className="inline-flex items-center gap-2 rounded border border-line px-3 py-2 text-sm">
                <ChevronUp size={16} />
                Up
              </button>
            )}
            {currentPath && (
              <button type="button" onClick={() => onSelect(currentPath)} className="inline-flex items-center gap-2 rounded bg-accent px-3 py-2 text-sm font-medium text-white">
                <Check size={16} />
                Select
              </button>
            )}
          </div>
          {loading ? (
            <p className="text-sm text-slate-600">Loading folders...</p>
          ) : (
            <div className="space-y-2">
              {(listing?.entries ?? []).length ? (listing?.entries ?? []).map((entry) => (
                <button
                  key={entry.path}
                  type="button"
                  onClick={() => void loadPath(entry.path)}
                  className="flex w-full items-center gap-3 rounded border border-line px-3 py-2 text-left text-sm hover:bg-panel"
                >
                  <FolderOpen size={17} className="text-accent" />
                  <span className="min-w-0 flex-1 truncate">{entry.name}</span>
                  <span className="hidden max-w-xs truncate text-xs text-slate-500 sm:block">{entry.path}</span>
                </button>
              )) : (
                <p className="rounded bg-panel px-3 py-2 text-sm text-slate-600">No folders found here.</p>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function toAbsoluteSeedboxPath(rootPath: string, value: string) {
  if (!rootPath || value.startsWith("/")) return value || rootPath;
  return value ? `${rootPath.replace(/\/$/, "")}/${value.replace(/^\//, "")}` : rootPath;
}

function toRuleRemotePath(rootPath: string, selectedPath: string) {
  const root = rootPath.replace(/\/$/, "");
  if (!root || !selectedPath.startsWith(root)) return selectedPath;
  return selectedPath.slice(root.length).replace(/^\//, "");
}

function Storage({ storage, setStorage }: { storage: StorageLocation[]; setStorage: (items: StorageLocation[]) => void }) {
  const [message, setMessage] = React.useState("");

  async function scanNow() {
    setMessage("Scanning...");
    const updated = await postJson<StorageLocation[]>("/api/storage/scan");
    setStorage(updated);
    setMessage("Storage scan complete.");
  }

  return (
    <section className="space-y-4">
      <div className="flex items-center gap-3">
        <button onClick={scanNow} className="rounded bg-accent px-4 py-2 text-sm font-medium text-white">Scan now</button>
        <p className="text-sm text-slate-600">{message}</p>
      </div>
      <Table
        rows={storage.map((item) => [
          item.name,
          String(item.kind),
          item.pathOrEndpoint,
          formatBytes(item.usedBytes),
          formatBytes(item.freeBytes),
          new Date(item.lastScannedAt).toLocaleString()
        ])}
        headers={["Name", "Kind", "Path/API", "Used", "Free", "Last scanned"]}
      />
    </section>
  );
}

function Downloads({ downloads }: { downloads: DownloadItem[] }) {
  return <Table rows={downloads.map((item) => [item.name, item.client, item.status, `${Math.round(item.progress * 100)}%`, formatBytes(item.sizeBytes), item.remotePath ?? ""])} headers={["Name", "Client", "Status", "Progress", "Size", "Remote path"]} />;
}

function Transfers({ transfers, settings }: { transfers: RemoteTransfer[]; settings: TransferSettings | null }) {
  return (
    <section className="space-y-4">
      <div className="grid gap-4 md:grid-cols-3">
        <Metric title="Auto download" value={settings?.enabled ? "Enabled" : "Disabled"} sub={settings?.sftpSeedbox.configured ? "SFTP configured" : "SFTP not configured"} />
        <Metric title="Local paths" value={String(settings?.localMediaPaths.length ?? 0)} sub="Media destinations" />
        <Metric title="Completed transfers" value={String(transfers.filter((item) => item.status === "Completed").length)} sub="Skipped files are tracked too" />
      </div>
      <Table
        rows={transfers.map((item) => [
          item.status,
          item.provider,
          item.remotePath,
          item.localPath,
          `${Math.round(getTransferProgress(item) * 100)}%`,
          `${formatBytes(item.bytesTransferred)} / ${formatBytes(item.totalBytes)}`,
          item.message ?? ""
        ])}
        headers={["Status", "Provider", "Remote path", "Local path", "Progress", "Bytes", "Message"]}
      />
      <div className="space-y-3">
        {transfers.filter((item) => item.status === "Queued" || item.status === "Running").map((item) => (
          <div key={item.id} className="rounded border border-line bg-white p-4">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2 text-sm">
              <span className="break-all font-medium">{item.remotePath}</span>
              <span>{Math.round(getTransferProgress(item) * 100)}% · {formatBytes(item.bytesTransferred)} / {formatBytes(item.totalBytes)}</span>
            </div>
            <div className="h-2 overflow-hidden rounded bg-panel">
              <div className="h-full bg-accent transition-all" style={{ width: `${Math.round(getTransferProgress(item) * 100)}%` }} />
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function getTransferProgress(transfer: RemoteTransfer) {
  if (transfer.totalBytes <= 0) {
    return transfer.status === "Completed" ? 1 : 0;
  }

  return Math.min(1, transfer.bytesTransferred / transfer.totalBytes);
}

function Cleanup({ candidates, setCandidates }: { candidates: CleanupCandidateSummary[]; setCandidates: (items: CleanupCandidateSummary[]) => void }) {
  const [message, setMessage] = React.useState("");

  async function generate() {
    const generated = await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/generate");
    setCandidates(generated);
  }

  async function approveDryRun(id: string) {
    await postJson(`/api/cleanup/candidates/${id}/approve`, { actor: "admin", dryRun: true });
    setCandidates(await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates"));
    setMessage("Dry-run approval recorded.");
  }

  async function reject(id: string) {
    await postJson(`/api/cleanup/candidates/${id}/reject`, { actor: "admin" });
    setCandidates(await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates"));
    setMessage("Candidate rejected.");
  }

  return (
    <section className="space-y-4">
      <button onClick={generate} className="rounded bg-accent px-4 py-2 text-sm font-medium text-white">Generate recommendations</button>
      {message && <p className="text-sm text-slate-600">{message}</p>}
      <div className="space-y-3">
        {candidates.map((item) => (
          <div key={item.id} className="rounded border border-line bg-white p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="break-all font-medium">{item.path}</p>
                <p className="mt-1 text-sm text-slate-600">{item.reason}</p>
              </div>
              <span className={`rounded px-2 py-1 text-xs ${item.confidence === "Uncertain" ? "bg-amber-100 text-warn" : "bg-emerald-100 text-accent"}`}>{item.confidence}</span>
            </div>
            <div className="mt-4 flex flex-wrap items-center gap-2 text-sm">
              <span>{formatBytes(item.sizeBytes)}</span>
              <span className="text-slate-500">Dry-run: {String(item.dryRun)}</span>
              <button onClick={() => approveDryRun(item.id)} className="rounded border border-line px-3 py-1">Approve dry-run</button>
              <button onClick={() => reject(item.id)} className="rounded border border-line px-3 py-1">Reject</button>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function SettingsPanel({ transferSettings, jobs }: { transferSettings: TransferSettings | null; jobs: JobRun[] }) {
  return (
    <section className="space-y-4">
      <div className="rounded border border-line bg-white p-5">
        <h3 className="font-medium">Runtime configuration</h3>
        <p className="mt-2 text-sm text-slate-600">Configure PostgreSQL, torrent client, SFTP seedbox access, storage paths, and dry-run behavior with environment variables in Docker Compose.</p>
      </div>
      <div className="rounded border border-line bg-white p-5">
        <h3 className="font-medium">Auto download</h3>
        <div className="mt-3 grid gap-2 text-sm text-slate-700 md:grid-cols-2">
          <p>Enabled: {String(transferSettings?.enabled ?? false)}</p>
          <p>SFTP configured: {String(transferSettings?.sftpSeedbox.configured ?? false)}</p>
          <p>Host: {transferSettings?.sftpSeedbox.host || "not set"}</p>
          <p>Local paths: {String(transferSettings?.localMediaPaths.length ?? 0)}</p>
        </div>
      </div>
      <div className="rounded border border-line bg-white p-5">
        <h3 className="font-medium">Job history</h3>
        <div className="mt-3 space-y-2 text-sm text-slate-700">
          {jobs.length ? jobs.slice(0, 10).map((job) => (
            <p key={job.id} className="rounded bg-panel px-3 py-2">
              {job.jobType} - {job.status} - {new Date(job.startedAt).toLocaleString()}{job.errorMessage ? ` - ${job.errorMessage}` : ""}
            </p>
          )) : <p className="text-slate-500">No jobs recorded yet.</p>}
        </div>
      </div>
    </section>
  );
}

function Metric({ title, value, sub }: { title: string; value: string; sub: string }) {
  return (
    <div className="rounded border border-line bg-white p-4">
      <p className="text-sm text-slate-600">{title}</p>
      <p className="mt-2 text-2xl font-semibold">{value}</p>
      <p className="mt-1 text-xs text-slate-500">{sub}</p>
    </div>
  );
}

function TwoColumn({ leftTitle, leftItems, rightTitle, rightItems }: { leftTitle: string; leftItems: string[]; rightTitle: string; rightItems: string[] }) {
  return (
    <div className="grid gap-4 xl:grid-cols-2">
      <ListPanel title={leftTitle} items={leftItems} />
      <ListPanel title={rightTitle} items={rightItems} />
    </div>
  );
}

function ListPanel({ title, items }: { title: string; items: string[] }) {
  return (
    <div className="rounded border border-line bg-white p-4">
      <h3 className="font-medium">{title}</h3>
      <div className="mt-3 space-y-2 text-sm text-slate-700">
        {items.length ? items.map((item) => <p key={item} className="break-words rounded bg-panel px-3 py-2">{item}</p>) : <p className="text-slate-500">No items yet.</p>}
      </div>
    </div>
  );
}

function Table({ headers, rows }: { headers: string[]; rows: string[][] }) {
  return (
    <div className="overflow-hidden rounded border border-line bg-white">
      <div className="overflow-x-auto">
        <table className="min-w-full text-left text-sm">
          <thead className="bg-panel text-slate-600">
            <tr>{headers.map((header) => <th key={header} className="px-4 py-3 font-medium">{header}</th>)}</tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <tr key={`${row[0]}-${index}`} className="border-t border-line">
                {row.map((cell, cellIndex) => <td key={`${cell}-${cellIndex}`} className="max-w-sm break-words px-4 py-3">{cell}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<App />);
