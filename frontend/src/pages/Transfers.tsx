import { FolderDown } from "lucide-react";
import { Card, CardHeader } from "../components/Card";
import { Metric } from "../components/Metric";
import { ActivityRow } from "../components/ActivityRow";
import { EmptyState } from "../components/EmptyState";
import { getDisplayTitle } from "../format";
import type { RemoteTransfer, TransferSettings } from "../types";

export function Transfers({ transfers, settings }: { transfers: RemoteTransfer[]; settings: TransferSettings | null }) {
  return (
    <section className="space-y-4">
      <div className="grid gap-4 md:grid-cols-3">
        <Metric icon={FolderDown} title="Auto download" value={settings?.enabled ? "Enabled" : "Disabled"} sub={settings?.sftpSeedbox.configured ? "SFTP configured" : "SFTP not configured"} />
        <Metric icon={FolderDown} title="Local paths" value={String(settings?.localMediaPaths.length ?? 0)} sub="Media destinations" />
        <Metric icon={FolderDown} title="Completed transfers" value={String(transfers.filter((item) => item.status === "Completed").length)} sub="Skipped files are tracked too" />
      </div>
      <Card>
        <CardHeader title="Transfer queue" description="Every seedbox transfer Flowarr has queued, downloaded, or skipped." />
        {transfers.length ? (
          <div className="space-y-2">
            {transfers.map((item) => (
              <ActivityRow
                key={item.id}
                title={getDisplayTitle(item.remotePath, item.localPath)}
                status={item.status}
                bytesTransferred={item.bytesTransferred}
                totalBytes={item.totalBytes}
                message={item.message}
              />
            ))}
          </div>
        ) : (
          <EmptyState icon={FolderDown} message="No transfers yet." />
        )}
      </Card>
    </section>
  );
}
