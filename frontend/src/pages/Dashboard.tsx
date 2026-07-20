import React from "react";
import { ChevronLeft, ChevronRight, Clock, Copy, Download, HardDrive, ImportIcon, Server, Trash2 } from "lucide-react";
import { fetchJson, postEmpty } from "../api";
import { Card, CardHeader } from "../components/Card";
import { EmptyState } from "../components/EmptyState";
import { Metric } from "../components/Metric";
import { ActivityRow } from "../components/ActivityRow";
import { StatusBadge } from "../components/StatusBadge";
import { extractSeasonLabel, formatBytes, formatRelativeTime, getDisplayTitle, isTransferActive } from "../format";
import type { DashboardSummary, RemoteTransfer } from "../types";

export function Dashboard({
  dashboard,
  transfers,
  setTransfers,
  torrentClientConfigured
}: {
  dashboard: DashboardSummary | null;
  transfers: RemoteTransfer[];
  setTransfers: (items: RemoteTransfer[]) => void;
  torrentClientConfigured: boolean;
}) {
  const activeTransfers = transfers.filter(isTransferActive);
  const allDownloads = dashboard?.downloads ?? [];
  const torrentDownloading = allDownloads.filter((item) => item.status === "Downloading").length;
  const activeCount = activeTransfers.length + torrentDownloading;

  const torrentsPerPage = 5;
  const [torrentPage, setTorrentPage] = React.useState(0);
  const torrentPageCount = Math.max(1, Math.ceil(allDownloads.length / torrentsPerPage));
  const currentTorrentPage = Math.min(torrentPage, torrentPageCount - 1);
  const pagedDownloads = allDownloads.slice(currentTorrentPage * torrentsPerPage, currentTorrentPage * torrentsPerPage + torrentsPerPage);

  async function cancelTransfer(transfer: RemoteTransfer) {
    if (transfer.status === "Running" && !window.confirm(`Cancel "${getDisplayTitle(transfer.remotePath, transfer.localPath)}"? The partially downloaded file will be deleted.`)) {
      return;
    }

    await postEmpty(`/api/transfers/${transfer.id}/cancel`);
    setTransfers(await fetchJson<RemoteTransfer[]>("/api/transfers"));
  }

  return (
    <section className="space-y-5">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Metric icon={HardDrive} title="Local used" value={formatBytes(dashboard?.localStorage.usedBytes ?? 0)} sub={`${formatBytes(dashboard?.localStorage.freeBytes ?? 0)} free`} />
        <Metric icon={Server} title="Seedbox used" value={formatBytes(dashboard?.seedboxStorage.usedBytes ?? 0)} sub={`${formatBytes(dashboard?.seedboxStorage.freeBytes ?? 0)} free`} />
        <Metric icon={Download} title="Active transfers" value={String(activeCount)} sub="SFTP + torrent client combined" />
        <Metric
          icon={Trash2}
          title="Cleanup candidates"
          value={String((dashboard?.cleanupStats.pendingDuplicateCount ?? 0) + (dashboard?.cleanupStats.pendingStaleCount ?? 0))}
          sub="Manual review required"
        />
      </div>

      <Card>
        <CardHeader title="Activity" description="Files currently queued or downloading from your seedbox." />
        {activeTransfers.length ? (
          <div className="space-y-2">
            {activeTransfers.map((transfer) => (
              <ActivityRow
                key={transfer.id}
                title={getDisplayTitle(transfer.remotePath, transfer.localPath)}
                seasonLabel={extractSeasonLabel(transfer.currentFile ?? "", transfer.remotePath, transfer.localPath)}
                status={transfer.status}
                bytesTransferred={transfer.bytesTransferred}
                totalBytes={transfer.totalBytes}
                onCancel={() => void cancelTransfer(transfer)}
              />
            ))}
          </div>
        ) : (
          <EmptyState icon={Download} message="No active transfers right now." />
        )}
      </Card>

      <div className="grid gap-4 xl:grid-cols-2">
        <Card className={torrentClientConfigured ? undefined : "xl:col-span-2"}>
          <CardHeader title="Recently imported" />
          {dashboard?.recentImports.length ? (
            <div className="space-y-2">
              {dashboard.recentImports.map((item) => (
                <div key={item.mediaItemId} className="flex items-center gap-3 rounded-md border border-line bg-surfaceMuted/40 p-3">
                  <div className="grid h-9 w-9 flex-none place-items-center rounded-md bg-accentMuted text-accent">
                    <ImportIcon size={16} />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="flex min-w-0 items-center gap-2">
                      {extractSeasonLabel(item.title) && (
                        <span className="flex-none rounded-full bg-accentMuted px-2 py-0.5 text-xs font-medium text-accent">{extractSeasonLabel(item.title)}</span>
                      )}
                      <p className="min-w-0 truncate text-sm font-medium text-ink">{item.title}</p>
                    </div>
                    <p className="text-xs text-muted">{formatRelativeTime(item.importedAt)}</p>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState icon={ImportIcon} message="No imports yet." />
          )}
        </Card>

        {torrentClientConfigured && (
          <Card>
            <CardHeader title="Torrent client" />
            {allDownloads.length ? (
              <div className="space-y-2">
                {pagedDownloads.map((item) => (
                  <div key={item.id} className="flex items-center justify-between gap-3 rounded-md border border-line bg-surfaceMuted/40 p-3">
                    <div className="flex min-w-0 flex-1 items-center gap-2">
                      {extractSeasonLabel(item.name) && (
                        <span className="flex-none rounded-full bg-accentMuted px-2 py-0.5 text-xs font-medium text-accent">{extractSeasonLabel(item.name)}</span>
                      )}
                      <p className="min-w-0 truncate text-sm text-ink">{item.name}</p>
                    </div>
                    <div className="flex flex-none items-center gap-2">
                      <span className="text-xs text-muted">{Math.round(item.progress * 100)}%</span>
                      <StatusBadge status={item.status} />
                    </div>
                  </div>
                ))}
                {torrentPageCount > 1 && (
                  <div className="flex items-center justify-between pt-1">
                    <button
                      type="button"
                      onClick={() => setTorrentPage((page) => Math.max(0, page - 1))}
                      disabled={currentTorrentPage === 0}
                      className="flex items-center gap-1 rounded-md border border-line px-2 py-1 text-xs text-ink hover:bg-surfaceMuted disabled:cursor-not-allowed disabled:opacity-40"
                    >
                      <ChevronLeft size={14} /> Prev
                    </button>
                    <span className="text-xs text-muted">Page {currentTorrentPage + 1} of {torrentPageCount}</span>
                    <button
                      type="button"
                      onClick={() => setTorrentPage((page) => Math.min(torrentPageCount - 1, page + 1))}
                      disabled={currentTorrentPage === torrentPageCount - 1}
                      className="flex items-center gap-1 rounded-md border border-line px-2 py-1 text-xs text-ink hover:bg-surfaceMuted disabled:cursor-not-allowed disabled:opacity-40"
                    >
                      Next <ChevronRight size={14} />
                    </button>
                  </div>
                )}
              </div>
            ) : (
              <EmptyState icon={Download} message="No downloads right now." />
            )}
          </Card>
        )}
      </div>

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader title="Cleanup" description="Space that could be reclaimed right now." />
          <div className="grid grid-cols-2 gap-3 text-sm">
            <div className="rounded-md border border-line bg-surfaceMuted/40 p-3">
              <p className="text-xs text-muted">Duplicates</p>
              <p className="mt-1 font-medium text-ink">{dashboard?.cleanupStats.pendingDuplicateCount ?? 0} file(s)</p>
              <p className="text-xs text-muted">{formatBytes(dashboard?.cleanupStats.pendingDuplicateBytes ?? 0)}</p>
            </div>
            <div className="rounded-md border border-line bg-surfaceMuted/40 p-3">
              <p className="text-xs text-muted">Stale / unwatched</p>
              <p className="mt-1 font-medium text-ink">{dashboard?.cleanupStats.pendingStaleCount ?? 0} file(s)</p>
              <p className="text-xs text-muted">{formatBytes(dashboard?.cleanupStats.pendingStaleBytes ?? 0)}</p>
            </div>
            <div className="rounded-md border border-line bg-surfaceMuted/40 p-3">
              <p className="text-xs text-muted">Space freed to date</p>
              <p className="mt-1 font-medium text-ink">{formatBytes(dashboard?.cleanupStats.lifetimeFreedBytes ?? 0)}</p>
            </div>
            <div className="rounded-md border border-line bg-surfaceMuted/40 p-3">
              <p className="text-xs text-muted">Last scan</p>
              <p className="mt-1 font-medium text-ink">{dashboard?.cleanupStats.lastScanAt ? formatRelativeTime(dashboard.cleanupStats.lastScanAt) : "Never"}</p>
              {dashboard?.cleanupStats.nextScanAt && <p className="text-xs text-muted">Next: {formatRelativeTime(dashboard.cleanupStats.nextScanAt)}</p>}
            </div>
          </div>
        </Card>

        <Card>
          <CardHeader title="Cleanup review" />
          {dashboard?.cleanupCandidates.length ? (
            <div className="space-y-2">
              {dashboard.cleanupCandidates.map((item) => (
                <div key={item.id} className="flex items-center justify-between gap-3 rounded-md border border-line bg-surfaceMuted/40 p-3">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm text-ink">{item.seriesTitle ? `${item.seriesTitle}${formatSeasonEpisode(item.seasonNumber, item.episodeNumber)}` : basenameOf(item.path)}</p>
                    {item.seriesTitle && <p className="truncate text-xs text-muted">{basenameOf(item.path)}</p>}
                    {item.reasonType === "Duplicate" && <span className="inline-flex items-center gap-1 text-xs text-muted"><Copy size={12} /> Duplicate</span>}
                    {item.reasonType === "Stale" && <span className="inline-flex items-center gap-1 text-xs text-muted"><Clock size={12} /> Stale / unwatched</span>}
                  </div>
                  <StatusBadge status={item.confidence} />
                </div>
              ))}
            </div>
          ) : (
            <EmptyState icon={Trash2} message="Nothing pending review." />
          )}
        </Card>
      </div>
    </section>
  );
}

function basenameOf(path: string) {
  return path.replace(/[/\\]+$/, "").split(/[/\\]/).pop() || path;
}

function formatSeasonEpisode(seasonNumber?: number, episodeNumber?: number) {
  if (typeof seasonNumber !== "number") return "";
  if (typeof episodeNumber === "number") return ` - Season ${seasonNumber}, Episode ${episodeNumber}`;
  return ` - Season ${seasonNumber}`;
}
