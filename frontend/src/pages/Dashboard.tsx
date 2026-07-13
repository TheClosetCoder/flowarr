import { Download, HardDrive, ImportIcon, Server, Trash2 } from "lucide-react";
import { Card, CardHeader } from "../components/Card";
import { EmptyState } from "../components/EmptyState";
import { Metric } from "../components/Metric";
import { ActivityRow } from "../components/ActivityRow";
import { StatusBadge } from "../components/StatusBadge";
import { formatBytes, formatRelativeTime, getDisplayTitle, isTransferActive } from "../format";
import type { DashboardSummary, RemoteTransfer } from "../types";

export function Dashboard({ dashboard, transfers }: { dashboard: DashboardSummary | null; transfers: RemoteTransfer[] }) {
  const activeTransfers = transfers.filter(isTransferActive);
  const torrentDownloading = (dashboard?.downloads ?? []).filter((item) => item.status === "Downloading").length;
  const activeCount = activeTransfers.length + torrentDownloading;

  return (
    <section className="space-y-5">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Metric icon={HardDrive} title="Local used" value={formatBytes(dashboard?.localStorage.usedBytes ?? 0)} sub={`${formatBytes(dashboard?.localStorage.freeBytes ?? 0)} free`} />
        <Metric icon={Server} title="Seedbox used" value={formatBytes(dashboard?.seedboxStorage.usedBytes ?? 0)} sub={`${formatBytes(dashboard?.seedboxStorage.freeBytes ?? 0)} free`} />
        <Metric icon={Download} title="Active transfers" value={String(activeCount)} sub="SFTP + torrent client combined" />
        <Metric icon={Trash2} title="Cleanup candidates" value={String(dashboard?.cleanupCandidates.length ?? 0)} sub="Manual review required" />
      </div>

      <Card>
        <CardHeader title="Activity" description="Files currently queued or downloading from your seedbox." />
        {activeTransfers.length ? (
          <div className="space-y-2">
            {activeTransfers.map((transfer) => (
              <ActivityRow
                key={transfer.id}
                title={getDisplayTitle(transfer.remotePath, transfer.localPath)}
                status={transfer.status}
                bytesTransferred={transfer.bytesTransferred}
                totalBytes={transfer.totalBytes}
              />
            ))}
          </div>
        ) : (
          <EmptyState icon={Download} message="No active transfers right now." />
        )}
      </Card>

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader title="Recently imported" />
          {dashboard?.recentImports.length ? (
            <div className="space-y-2">
              {dashboard.recentImports.map((item) => (
                <div key={item.mediaItemId} className="flex items-center gap-3 rounded-md border border-line bg-surfaceMuted/40 p-3">
                  <div className="grid h-9 w-9 flex-none place-items-center rounded-md bg-accentMuted text-accent">
                    <ImportIcon size={16} />
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-ink">{item.title}</p>
                    <p className="text-xs text-muted">{formatRelativeTime(item.importedAt)}</p>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState icon={ImportIcon} message="No imports yet." />
          )}
        </Card>

        <Card>
          <CardHeader title="Torrent client" />
          {(dashboard?.downloads ?? []).length ? (
            <div className="space-y-2">
              {(dashboard?.downloads ?? []).map((item) => (
                <div key={item.id} className="flex items-center justify-between gap-3 rounded-md border border-line bg-surfaceMuted/40 p-3">
                  <p className="min-w-0 flex-1 truncate text-sm text-ink">{item.name}</p>
                  <div className="flex flex-none items-center gap-2">
                    <span className="text-xs text-muted">{Math.round(item.progress * 100)}%</span>
                    <StatusBadge status={item.status} />
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState icon={Download} message="No torrent client configured." />
          )}
        </Card>
      </div>

      <Card>
        <CardHeader title="Cleanup review" />
        {dashboard?.cleanupCandidates.length ? (
          <div className="space-y-2">
            {dashboard.cleanupCandidates.map((item) => (
              <div key={item.id} className="flex items-center justify-between gap-3 rounded-md border border-line bg-surfaceMuted/40 p-3">
                <p className="min-w-0 flex-1 truncate text-sm text-ink">{item.path}</p>
                <StatusBadge status={item.confidence} />
              </div>
            ))}
          </div>
        ) : (
          <EmptyState icon={Trash2} message="Nothing pending review." />
        )}
      </Card>
    </section>
  );
}
