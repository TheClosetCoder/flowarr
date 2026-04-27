import React from "react";
import { createRoot } from "react-dom/client";
import { Activity, Download, FolderDown, HardDrive, Plug, Settings, ShieldCheck, Trash2 } from "lucide-react";
import "./styles.css";

type DashboardSummary = {
  localStorage: StorageSummary;
  seedboxStorage: StorageSummary;
  activeDownloads: number;
  recentImports: RecentImport[];
  cleanupCandidates: CleanupCandidateSummary[];
};

type StorageSummary = { usedBytes: number; freeBytes: number };
type RecentImport = { mediaItemId: string; title: string; importedAt: string };
type CleanupCandidateSummary = { id: string; path: string; sizeBytes: number; reason: string; confidence: string; dryRun: boolean };
type DownloadItem = { id: string; name: string; client: string; status: string; progress: number; sizeBytes: number; remotePath?: string };
type StorageLocation = { id: string; name: string; kind: string | number; pathOrEndpoint: string; usedBytes: number; freeBytes: number; lastScannedAt: string };
type RemoteTransfer = { id: string; provider: string; remotePath: string; localPath: string; bytesTransferred: number; status: string; message?: string; createdAt: string; completedAt?: string };
type TransferSettings = { enabled: boolean; localImportPath: string; overwriteExisting: boolean; allowedExtensions: string[]; sftpSeedbox: { configured: boolean; host: string; port: number; remoteRootPath: string; usesPrivateKey: boolean } };

const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";

function formatBytes(value: number) {
  if (!value) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${(value / 1024 ** index).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function App() {
  const [activePage, setActivePage] = React.useState("Dashboard");
  const [dashboard, setDashboard] = React.useState<DashboardSummary | null>(null);
  const [downloads, setDownloads] = React.useState<DownloadItem[]>([]);
  const [storage, setStorage] = React.useState<StorageLocation[]>([]);
  const [candidates, setCandidates] = React.useState<CleanupCandidateSummary[]>([]);
  const [transfers, setTransfers] = React.useState<RemoteTransfer[]>([]);
  const [transferSettings, setTransferSettings] = React.useState<TransferSettings | null>(null);

  React.useEffect(() => {
    void Promise.all([
      fetchJson<DashboardSummary>("/api/dashboard").then(setDashboard),
      fetchJson<DownloadItem[]>("/api/downloads").then(setDownloads),
      fetchJson<StorageLocation[]>("/api/storage").then(setStorage),
      fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates").then(setCandidates),
      fetchJson<RemoteTransfer[]>("/api/transfers").then(setTransfers),
      fetchJson<TransferSettings>("/api/transfers/settings").then(setTransferSettings)
    ]);
  }, []);

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
              onClick={() => setActivePage(page.name)}
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
            <div className="flex rounded border border-line bg-white p-1 lg:hidden">
              {pages.map((page) => (
                <button
                  key={page.name}
                  title={page.name}
                  onClick={() => setActivePage(page.name)}
                  className={`grid h-10 w-10 place-items-center rounded ${activePage === page.name ? "bg-accent text-white" : ""}`}
                >
                  <page.icon size={18} />
                </button>
              ))}
            </div>
          </div>

          {activePage === "Dashboard" && <Dashboard dashboard={dashboard} />}
          {activePage === "Connections" && <Connections />}
          {activePage === "Storage" && <Storage storage={storage} />}
          {activePage === "Downloads" && <Downloads downloads={downloads} />}
          {activePage === "Transfers" && <Transfers transfers={transfers} settings={transferSettings} />}
          {activePage === "Cleanup" && <Cleanup candidates={candidates} setCandidates={setCandidates} />}
          {activePage === "Settings" && <SettingsPanel transferSettings={transferSettings} />}
        </div>
      </main>
    </div>
  );
}

async function fetchJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBase}${path}`);
  if (!response.ok) throw new Error(`Request failed: ${path}`);
  return response.json();
}

function Dashboard({ dashboard }: { dashboard: DashboardSummary | null }) {
  return (
    <section className="space-y-5">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Metric title="Local used" value={formatBytes(dashboard?.localStorage.usedBytes ?? 0)} sub={`${formatBytes(dashboard?.localStorage.freeBytes ?? 0)} free`} />
        <Metric title="Seedbox used" value={formatBytes(dashboard?.seedboxStorage.usedBytes ?? 0)} sub={`${formatBytes(dashboard?.seedboxStorage.freeBytes ?? 0)} free`} />
        <Metric title="Active downloads" value={String(dashboard?.activeDownloads ?? 0)} sub="qBittorrent mock until configured" />
        <Metric title="Cleanup candidates" value={String(dashboard?.cleanupCandidates.length ?? 0)} sub="Manual review required" />
      </div>
      <TwoColumn
        leftTitle="Recent imports"
        leftItems={(dashboard?.recentImports ?? []).map((item) => `${item.title} imported ${new Date(item.importedAt).toLocaleString()}`)}
        rightTitle="Cleanup review"
        rightItems={(dashboard?.cleanupCandidates ?? []).map((item) => `${item.path} - ${item.confidence}`)}
      />
    </section>
  );
}

function Connections() {
  const integrations = ["qBittorrent", "Radarr", "SFTP seedbox", "Sonarr", "Plex", "Jellyfin"];
  return (
    <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      {integrations.map((name) => (
        <div key={name} className="rounded border border-line bg-white p-4">
          <div className="flex items-center gap-3">
            <Plug className="text-accent" size={20} />
            <h3 className="font-medium">{name}</h3>
          </div>
          <p className="mt-3 text-sm text-slate-600">Uses mock data until credentials are configured through environment variables.</p>
        </div>
      ))}
    </section>
  );
}

function Storage({ storage }: { storage: StorageLocation[] }) {
  return <Table rows={storage.map((item) => [item.name, String(item.kind), item.pathOrEndpoint, formatBytes(item.usedBytes), formatBytes(item.freeBytes)])} headers={["Name", "Kind", "Path/API", "Used", "Free"]} />;
}

function Downloads({ downloads }: { downloads: DownloadItem[] }) {
  return <Table rows={downloads.map((item) => [item.name, item.client, item.status, `${Math.round(item.progress * 100)}%`, formatBytes(item.sizeBytes), item.remotePath ?? ""])} headers={["Name", "Client", "Status", "Progress", "Size", "Remote path"]} />;
}

function Transfers({ transfers, settings }: { transfers: RemoteTransfer[]; settings: TransferSettings | null }) {
  return (
    <section className="space-y-4">
      <div className="grid gap-4 md:grid-cols-3">
        <Metric title="Auto download" value={settings?.enabled ? "Enabled" : "Disabled"} sub={settings?.sftpSeedbox.configured ? "SFTP configured" : "SFTP not configured"} />
        <Metric title="Plex import path" value={settings?.localImportPath ?? "/plex/import"} sub="Downloaded files land here" />
        <Metric title="Completed transfers" value={String(transfers.filter((item) => item.status === "Completed").length)} sub="Skipped files are tracked too" />
      </div>
      <Table
        rows={transfers.map((item) => [
          item.status,
          item.provider,
          item.remotePath,
          item.localPath,
          formatBytes(item.bytesTransferred),
          item.message ?? ""
        ])}
        headers={["Status", "Provider", "Remote path", "Local path", "Bytes", "Message"]}
      />
    </section>
  );
}

function Cleanup({ candidates, setCandidates }: { candidates: CleanupCandidateSummary[]; setCandidates: (items: CleanupCandidateSummary[]) => void }) {
  async function generate() {
    const generated = await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/generate");
    setCandidates(generated);
  }

  return (
    <section className="space-y-4">
      <button onClick={generate} className="rounded bg-accent px-4 py-2 text-sm font-medium text-white">Generate recommendations</button>
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
              <button className="rounded border border-line px-3 py-1">Approve dry-run</button>
              <button className="rounded border border-line px-3 py-1">Reject</button>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function SettingsPanel({ transferSettings }: { transferSettings: TransferSettings | null }) {
  return (
    <section className="space-y-4">
      <div className="rounded border border-line bg-white p-5">
        <h3 className="font-medium">Runtime configuration</h3>
        <p className="mt-2 text-sm text-slate-600">Configure PostgreSQL, qBittorrent, Radarr, SFTP seedbox access, storage paths, and dry-run behavior with environment variables in Docker Compose.</p>
      </div>
      <div className="rounded border border-line bg-white p-5">
        <h3 className="font-medium">Auto download</h3>
        <div className="mt-3 grid gap-2 text-sm text-slate-700 md:grid-cols-2">
          <p>Enabled: {String(transferSettings?.enabled ?? false)}</p>
          <p>SFTP configured: {String(transferSettings?.sftpSeedbox.configured ?? false)}</p>
          <p>Host: {transferSettings?.sftpSeedbox.host || "not set"}</p>
          <p>Local import: {transferSettings?.localImportPath ?? "/plex/import"}</p>
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
