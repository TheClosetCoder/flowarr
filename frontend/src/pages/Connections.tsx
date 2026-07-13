import React from "react";
import { FolderOpen } from "lucide-react";
import { createClientId, putJson, postJson } from "../api";
import { navigateToConnectionTab } from "../routing";
import { Card } from "../components/Card";
import { Input } from "../components/Input";
import { SectionHeader } from "../components/SectionHeader";
import { LocalFolderBrowser } from "../components/LocalFolderBrowser";
import { SeedboxFolderBrowser } from "../components/SeedboxFolderBrowser";
import type { ArrSettings, ConnectionSettings, ConnectionTestResult, LocalMediaPath, SeedboxRoot, TransferRule } from "../types";

export function Connections({
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
      <Card>
        <p className="text-sm text-muted">Loading connection settings...</p>
        {error && <p className="mt-2 text-sm text-danger">{error}</p>}
      </Card>
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
        sourceRootPath: rule.sourceRootPath || "",
        deleteRemoteAfterDownload: rule.deleteRemoteAfterDownload || false
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
        <Card>
          <SectionHeader
            title="Torrent client"
            description="Your download client, such as Deluge or qBittorrent. Flowarr reads torrent status and remote paths from here."
          />
          <div className="mt-4 grid gap-3">
            <label className="flex items-center gap-2 text-sm text-ink">
              <input type="checkbox" checked={qbittorrent.enabled} onChange={(event) => setQbittorrent({ ...qbittorrent, enabled: event.target.checked })} />
              Enabled
            </label>
            <label className="grid gap-1 text-sm">
              <span className="text-muted">Client</span>
              <select
                value={qbittorrent.clientType || "qBittorrent"}
                onChange={(event) => setQbittorrent({ ...qbittorrent, clientType: event.target.value })}
                className="rounded-md border border-line bg-surfaceMuted px-3 py-2 text-ink outline-none focus:border-accent"
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
            <p className="text-xs text-muted">Leave blank to use the saved encrypted password. Typed passwords are sent once to the Flowarr API for save/test.</p>
            {qbittorrent.hasPassword && (
              <label className="flex items-center gap-2 text-sm text-muted">
                <input type="checkbox" checked={qbittorrent.clearPassword} onChange={(event) => setQbittorrent({ ...qbittorrent, clearPassword: event.target.checked })} />
                Clear saved password
              </label>
            )}
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={saveTorrentClient} className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg">Save torrent client</button>
              <button type="button" onClick={testTorrentClient} disabled={testingTorrent} className="rounded-md border border-line px-4 py-2 text-sm font-medium text-ink hover:bg-surfaceMuted disabled:cursor-wait disabled:opacity-60">
                {testingTorrent ? "Testing..." : "Test connection"}
              </button>
            </div>
            {torrentTest && <ConnectionTestStatus result={torrentTest} pending={testingTorrent} />}
          </div>
        </Card>
      )}

      {activeTab === "sftp" && (
        <Card>
          <SectionHeader
            title="SFTP seedbox"
            description="SFTP access to your seedbox files. Flowarr uses this for browsing seedbox roots, scanning storage, and downloading completed files."
          />
          <div className="mt-4 grid gap-3">
            <label className="flex items-center gap-2 text-sm text-ink">
              <input type="checkbox" checked={sftpSeedbox.enabled} onChange={(event) => setSftpSeedbox({ ...sftpSeedbox, enabled: event.target.checked })} />
              Enabled
            </label>
            <Input label="Host" value={sftpSeedbox.host} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, host: value })} placeholder="your-ultra-sftp-host" />
            <Input label="Port" value={String(sftpSeedbox.port)} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, port: Number(value) || 22 })} />
            <Input label="Username" value={sftpSeedbox.username} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, username: value })} />
            <Input label={sftpSeedbox.hasPassword ? "Password (already saved)" : "Password"} type="password" value={sftpSeedbox.password} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, password: value })} placeholder={sftpSeedbox.hasPassword ? "Leave blank to keep current" : ""} />
            <p className="text-xs text-muted">Leave blank to use the saved encrypted password. Typed passwords are sent once to the Flowarr API for save/test.</p>
            <Input label="Private key path" value={sftpSeedbox.privateKeyPath} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, privateKeyPath: value })} placeholder="/keys/id_rsa" />
            <Input label={sftpSeedbox.hasPrivateKeyPassphrase ? "Key passphrase (already saved)" : "Key passphrase"} type="password" value={sftpSeedbox.privateKeyPassphrase} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, privateKeyPassphrase: value })} />
            <Input label="Legacy remote root path" value={sftpSeedbox.remoteRootPath} onChange={(value) => setSftpSeedbox({ ...sftpSeedbox, remoteRootPath: value })} placeholder="/home/user/files" />
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <h4 className="text-sm font-medium text-ink">Seedbox roots</h4>
                <button
                  type="button"
                  onClick={() => setSftpSeedbox({
                    ...sftpSeedbox,
                    seedboxRoots: [...sftpSeedbox.seedboxRoots, { id: createClientId(), enabled: true, name: "Downloads", kind: "Download", path: "/downloads" }]
                  })}
                  className="rounded-md border border-line px-3 py-1 text-sm text-ink hover:bg-surfaceMuted"
                >
                  Add root
                </button>
              </div>
              {sftpSeedbox.seedboxRoots.map((root, index) => (
                <div key={root.id ?? index} className="grid gap-2 rounded-md border border-line bg-surfaceMuted p-3 md:grid-cols-[auto_1fr_1fr_2fr_auto]">
                  <label className="flex items-center gap-2 text-sm text-ink">
                    <input type="checkbox" checked={root.enabled} onChange={(event) => updateSeedboxRoot(index, { enabled: event.target.checked })} />
                    Enabled
                  </label>
                  <Input label="Name" value={root.name} onChange={(value) => updateSeedboxRoot(index, { name: value })} placeholder="Movies" />
                  <label className="grid gap-1 text-sm">
                    <span className="text-muted">Kind</span>
                    <select value={root.kind} onChange={(event) => updateSeedboxRoot(index, { kind: event.target.value as "Download" | "Media" })} className="rounded-md border border-line bg-surface px-3 py-2 text-ink">
                      <option value="Download">Download</option>
                      <option value="Media">Media</option>
                    </select>
                  </label>
                  <Input label="Remote path" value={root.path} onChange={(value) => updateSeedboxRoot(index, { path: value })} placeholder="/media/Movies" />
                  <button type="button" onClick={() => setSftpSeedbox({ ...sftpSeedbox, seedboxRoots: sftpSeedbox.seedboxRoots.filter((_, rootIndex) => rootIndex !== index) })} className="self-end rounded-md border border-line px-3 py-2 text-sm text-ink hover:bg-surface">Remove</button>
                </div>
              ))}
            </div>
            <div className="grid gap-2 text-sm text-muted">
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
              <button type="button" onClick={saveSeedbox} className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg">Save seedbox</button>
              <button type="button" onClick={testSeedbox} disabled={testingSftp} className="rounded-md border border-line px-4 py-2 text-sm font-medium text-ink hover:bg-surfaceMuted disabled:cursor-wait disabled:opacity-60">
                {testingSftp ? "Testing..." : "Test connection"}
              </button>
            </div>
            {sftpTest && <ConnectionTestStatus result={sftpTest} pending={testingSftp} />}
          </div>
        </Card>
      )}

      {activeTab === "services" && (
      <div className="grid gap-4 xl:grid-cols-2">
        <ArrConnectionCard title="Radarr" value={radarr} test={radarrTest} onChange={setRadarr} onSave={() => saveArr("radarr")} onTest={() => testArr("radarr")} />
        <ArrConnectionCard title="Sonarr" value={sonarr} test={sonarrTest} onChange={setSonarr} onSave={() => saveArr("sonarr")} onTest={() => testArr("sonarr")} />
      </div>
      )}

      {activeTab === "sftp" && (
      <Card>
        <h3 className="font-medium text-ink">Auto download</h3>
        <div className="mt-4 grid gap-3 md:grid-cols-2">
          <label className="flex items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={autoDownload.enabled} onChange={(event) => setAutoDownload({ ...autoDownload, enabled: event.target.checked })} />
            Enabled
          </label>
          <label className="flex items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={autoDownload.overwriteExisting} onChange={(event) => setAutoDownload({ ...autoDownload, overwriteExisting: event.target.checked })} />
            Overwrite existing files
          </label>
          <LocalPathInput label="Legacy fallback destination" value={autoDownload.localImportPath} onChange={(value) => setAutoDownload({ ...autoDownload, localImportPath: value })} placeholder="/media/import" />
          <Input label="Allowed extensions" value={autoDownload.allowedExtensionsText} onChange={(value) => setAutoDownload({ ...autoDownload, allowedExtensionsText: value })} placeholder=".mkv, .mp4, .srt" />
        </div>
        <div className="mt-5 space-y-3">
          <div className="flex items-center justify-between gap-3">
            <SectionHeader
              size="sm"
              title="Local media paths"
              description="Local library folders inside the container, such as /media/Movies or /media/TV Shows. Flowarr scans these as your Plex/Jellyfin media destinations."
            />
            <button
              onClick={() => setAutoDownload({
                ...autoDownload,
                localMediaPaths: [...autoDownload.localMediaPaths, { id: createClientId(), enabled: true, name: "New path", path: "/media", role: "Mixed" }]
              })}
              className="rounded-md border border-line px-3 py-1 text-sm text-ink hover:bg-surfaceMuted"
            >
              Add path
            </button>
          </div>
          {autoDownload.localMediaPaths.map((path, index) => (
            <div key={path.id ?? index} className="grid gap-3 rounded-md border border-line bg-surfaceMuted p-3 md:grid-cols-[auto_1fr_1fr_1fr_auto]">
              <label className="flex items-center gap-2 text-sm text-ink">
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
                className="self-end rounded-md border border-line px-3 py-2 text-sm text-ink hover:bg-surface"
              >
                Remove
              </button>
            </div>
          ))}
        </div>
        <div className="mt-5 space-y-3">
          <div className="flex items-center justify-between gap-3">
            <SectionHeader
              size="sm"
              title="Download folder rules"
              description="Rules that map a seedbox source folder to a local media path. Use one rule per flow, for example Movies downloads to /media/Movies and TV downloads to /media/TV Shows."
            />
            <button
              onClick={() => setAutoDownload({
                ...autoDownload,
                transferRules: [...autoDownload.transferRules, { id: createClientId(), enabled: true, name: "New rule", remotePathPrefix: "", localPath: autoDownload.localMediaPaths[0]?.path || autoDownload.localImportPath || "/media/import", sourceRootKind: defaultSourceRoot?.kind ?? "Download", sourceRootPath: defaultSourceRoot?.path ?? "", deleteRemoteAfterDownload: false }]
              })}
              className="rounded-md border border-line px-3 py-1 text-sm text-ink hover:bg-surfaceMuted"
            >
              Add rule
            </button>
          </div>
          {autoDownload.transferRules.map((rule, index) => (
            <div key={rule.id ?? index} className="grid gap-3 rounded-md border border-line bg-surfaceMuted p-3 md:grid-cols-[auto_1fr_1fr_1fr_auto]">
              <label className="flex items-center gap-2 text-sm text-ink">
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
              <label className="flex items-center gap-2 text-sm text-ink">
                <input
                  type="checkbox"
                  checked={rule.deleteRemoteAfterDownload}
                  onChange={(event) => updateTransferRule(index, { deleteRemoteAfterDownload: event.target.checked })}
                />
                Delete remote after download
              </label>
              <button
                onClick={() => setAutoDownload({ ...autoDownload, transferRules: autoDownload.transferRules.filter((_, ruleIndex) => ruleIndex !== index) })}
                className="self-end rounded-md border border-line px-3 py-2 text-sm text-ink hover:bg-surface"
              >
                Remove
              </button>
            </div>
          ))}
        </div>
        <button onClick={saveAutoDownload} className="mt-4 rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg">Save auto-download</button>
      </Card>
      )}

      {activeTab === "plex" && <PlannedServicePanel name="Plex" />}
      {activeTab === "jellyfin" && <PlannedServicePanel name="Jellyfin" />}

      <div className="flex items-center gap-3">
        <p className="text-sm text-muted">{message}</p>
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
    <Card>
      <h3 className="font-medium text-ink">{title}</h3>
      <div className="mt-4 grid gap-3">
        <label className="flex items-center gap-2 text-sm text-ink">
          <input type="checkbox" checked={value.enabled} onChange={(event) => onChange({ ...value, enabled: event.target.checked })} />
          Enabled
        </label>
        <Input label="Base URL" value={value.baseUrl} onChange={(baseUrl) => onChange({ ...value, baseUrl })} placeholder={`https://your-${title.toLowerCase()}-host`} />
        <Input label={value.hasApiKey ? "API key (already saved)" : "API key"} type="password" value={value.apiKey} onChange={(apiKey) => onChange({ ...value, apiKey })} placeholder={value.hasApiKey ? "Leave blank to keep current" : ""} />
        {value.hasApiKey && (
          <label className="flex items-center gap-2 text-sm text-muted">
            <input type="checkbox" checked={value.clearApiKey} onChange={(event) => onChange({ ...value, clearApiKey: event.target.checked })} />
            Clear saved API key
          </label>
        )}
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={onSave} className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg">Save {title}</button>
          <button type="button" onClick={onTest} className="rounded-md border border-line px-4 py-2 text-sm font-medium text-ink hover:bg-surfaceMuted">Test connection</button>
        </div>
        {test && <ConnectionTestStatus result={test} pending={test.message.startsWith("Testing")} />}
      </div>
    </Card>
  );
}

function ConnectionTestStatus({ result, pending }: { result: ConnectionTestResult; pending: boolean }) {
  const styles = pending
    ? "border-accent/30 bg-accentMuted text-accent"
    : result.ok
      ? "border-success/30 bg-successMuted text-success"
      : "border-danger/30 bg-dangerMuted text-danger";

  return (
    <div className={`rounded-md border px-3 py-2 text-sm ${styles}`}>
      {result.message}
    </div>
  );
}

function ConnectionTabs({ activeTab, tabs, onChange }: { activeTab: string; tabs: { id: string; label: string }[]; onChange: (tab: string) => void }) {
  return (
    <div className="overflow-x-auto rounded-lg border border-line bg-surface p-1">
      <div className="flex min-w-max gap-1">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            type="button"
            onClick={() => onChange(tab.id)}
            className={`rounded-md px-4 py-2 text-sm font-medium ${activeTab === tab.id ? "bg-accentMuted text-accent" : "text-muted hover:bg-surfaceMuted hover:text-ink"}`}
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
    <Card>
      <SectionHeader
        title={name}
        description={`${name} integration is planned. Flowarr will use it later for library awareness and playback-server status.`}
      />
      <p className="mt-3 text-sm text-muted">Not configured yet.</p>
    </Card>
  );
}

function SeedboxRootSelect({ roots, rule, onChange }: { roots: SeedboxRoot[]; rule: TransferRule; onChange: (root: SeedboxRoot | null) => void }) {
  return (
    <label className="grid gap-1 text-sm">
      <span className="text-muted">Seedbox source root</span>
      <select
        value={rule.sourceRootPath}
        onChange={(event) => onChange(roots.find((root) => root.path === event.target.value) ?? null)}
        className="rounded-md border border-line bg-surface px-3 py-2 text-ink"
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
      <span className="text-muted">{label}</span>
      <div className="flex gap-2">
        <input
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          className="min-w-0 flex-1 rounded-md border border-line bg-surfaceMuted px-3 py-2 text-ink outline-none focus:border-accent"
        />
        <button
          type="button"
          onClick={() => setOpen(true)}
          title={`Browse ${label}`}
          className="grid h-10 w-10 flex-none place-items-center rounded-md border border-line bg-surface text-muted hover:bg-surfaceMuted hover:text-ink"
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
      <span className="text-muted">{label}</span>
      <div className="flex gap-2">
        <input
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          className="min-w-0 flex-1 rounded-md border border-line bg-surfaceMuted px-3 py-2 text-ink outline-none focus:border-accent"
        />
        <button
          type="button"
          onClick={() => setOpen(true)}
          title={`Browse ${label}`}
          className="grid h-10 w-10 flex-none place-items-center rounded-md border border-line bg-surface text-muted hover:bg-surfaceMuted hover:text-ink"
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

function toAbsoluteSeedboxPath(rootPath: string, value: string) {
  if (!rootPath || value.startsWith("/")) return value || rootPath;
  return value ? `${rootPath.replace(/\/$/, "")}/${value.replace(/^\//, "")}` : rootPath;
}

function toRuleRemotePath(rootPath: string, selectedPath: string) {
  const root = rootPath.replace(/\/$/, "");
  if (!root || !selectedPath.startsWith(root)) return selectedPath;
  return selectedPath.slice(root.length).replace(/^\//, "");
}
