import React from "react";
import { Activity, Download, FolderDown, HardDrive, Settings, Trash2 } from "lucide-react";
import { fetchJson, postJson } from "./api";
import { getRouteFromHash, navigateToPage } from "./routing";
import { normalizeConnectionSettings } from "./settingsHelpers";
import { Sidebar, MobileNav, type PageDefinition } from "./components/Sidebar";
import { TopBar } from "./components/TopBar";
import { AuthScreen } from "./pages/AuthScreen";
import { Dashboard } from "./pages/Dashboard";
import { Connections } from "./pages/Connections";
import { Storage } from "./pages/Storage";
import { Downloads } from "./pages/Downloads";
import { Transfers } from "./pages/Transfers";
import { Cleanup } from "./pages/Cleanup";
import type {
  AuthStatus,
  CleanupCandidateSummary,
  ConnectionSettings,
  DashboardSummary,
  DownloadItem,
  JobRun,
  RawConnectionSettings,
  RemoteTransfer,
  StorageLocation,
  TransferSettings
} from "./types";

const pages: PageDefinition[] = [
  { name: "Dashboard", icon: Activity },
  { name: "Storage", icon: HardDrive },
  { name: "Downloads", icon: Download },
  { name: "Transfers", icon: FolderDown },
  { name: "Cleanup", icon: Trash2 },
  { name: "Settings", icon: Settings }
];

export function App() {
  const [auth, setAuth] = React.useState<AuthStatus | null>(null);
  const [authUnreachable, setAuthUnreachable] = React.useState(false);
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
      .then((status) => {
        setAuth(status);
        setAuthUnreachable(false);
      })
      .catch(() => setAuthUnreachable(true));
  }, []);

  React.useEffect(() => {
    if (!auth?.authenticated) {
      return;
    }

    void Promise.all([
      fetchJson<DashboardSummary>("/api/dashboard").then(setDashboard),
      fetchJson<DownloadItem[]>("/api/downloads").then(setDownloads),
      fetchJson<StorageLocation[]>("/api/storage").then(setStorage),
      fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates?status=PendingReview").then(setCandidates),
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
    if (activePage !== "Transfers" && activePage !== "Dashboard" && !hasActiveTransfers) {
      return;
    }

    // Lightweight, DB-only query - safe to poll frequently for live progress bars.
    const timer = window.setInterval(() => {
      void fetchJson<RemoteTransfer[]>("/api/transfers").then(setTransfers);
    }, 2000);

    return () => window.clearInterval(timer);
  }, [activePage, auth?.authenticated, transfers]);

  React.useEffect(() => {
    if (!auth?.authenticated || activePage !== "Dashboard") {
      return;
    }

    // /api/dashboard triggers a live SFTP + torrent client scan on every call, so it's refreshed
    // on a much slower cadence than the transfer queue above - the Activity section gets its live
    // progress from the /api/transfers poll instead.
    const timer = window.setInterval(() => {
      void fetchJson<DashboardSummary>("/api/dashboard").then(setDashboard);
    }, 30000);

    return () => window.clearInterval(timer);
  }, [activePage, auth?.authenticated]);

  React.useEffect(() => {
    const onHashChange = () => {
      const route = getRouteFromHash();
      setActivePage(route.page);
      setConnectionTab(route.connectionTab);
    };
    window.addEventListener("hashchange", onHashChange);
    return () => window.removeEventListener("hashchange", onHashChange);
  }, []);

  if (authUnreachable && !auth) {
    return (
      <div className="grid min-h-screen place-items-center bg-bg px-4 text-center">
        <div>
          <p className="text-lg font-semibold text-ink">Can't reach the Flowarr server</p>
          <p className="mt-2 text-sm text-muted">The backend didn't respond. Check that the container is running, then try again.</p>
          <button
            onClick={() => window.location.reload()}
            className="mt-4 rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg"
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

  if (!auth) {
    return <div className="grid min-h-screen place-items-center bg-bg text-sm text-muted">Loading Flowarr...</div>;
  }

  if (!auth.authenticated) {
    return <AuthScreen setupRequired={!auth.setupComplete} onAuthenticated={() => setAuth({ setupComplete: true, authenticated: true })} />;
  }

  function onNavigate(page: string) {
    navigateToPage(page, connectionTab);
  }

  return (
    <div className="min-h-screen bg-bg text-ink">
      <Sidebar pages={pages} activePage={activePage} onNavigate={onNavigate} />

      <main className="lg:pl-64">
        <div className="mx-auto max-w-7xl px-4 py-5 sm:px-6 lg:px-8">
          <div className="mb-4 flex justify-end lg:hidden">
            <MobileNav pages={pages} activePage={activePage} onNavigate={onNavigate} />
          </div>
          <TopBar
            activePage={activePage}
            onLogout={async () => {
              await postJson("/api/auth/logout");
              setAuth({ setupComplete: true, authenticated: false });
            }}
          />

          {activePage === "Dashboard" && (
            <Dashboard
              dashboard={dashboard}
              transfers={transfers}
              setTransfers={setTransfers}
              torrentClientConfigured={Boolean(connectionSettings?.qbittorrent.enabled && connectionSettings.qbittorrent.baseUrl)}
            />
          )}
          {activePage === "Storage" && <Storage storage={storage} setStorage={setStorage} />}
          {activePage === "Downloads" && <Downloads downloads={downloads} />}
          {activePage === "Transfers" && <Transfers transfers={transfers} settings={transferSettings} setTransfers={setTransfers} />}
          {activePage === "Cleanup" && <Cleanup candidates={candidates} setCandidates={setCandidates} />}
          {activePage === "Settings" && (
            <Connections
              settings={connectionSettings}
              error={settingsError}
              activeTab={connectionTab}
              onTabChange={setConnectionTab}
              onSaved={refreshSettings}
              transferSettings={transferSettings}
              jobs={jobs}
            />
          )}
        </div>
      </main>
    </div>
  );
}
