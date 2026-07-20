import { RefreshCw, ClipboardList } from "lucide-react";
import { Card, CardHeader } from "../components/Card";
import { EmptyState } from "../components/EmptyState";
import type { JobRun, TransferSettings } from "../types";

export function SettingsPanel({
  transferSettings,
  jobs,
  syncingJobId,
  syncMessage,
  onSyncNow
}: {
  transferSettings: TransferSettings | null;
  jobs: JobRun[];
  syncingJobId: string | null;
  syncMessage: string;
  onSyncNow: () => void;
}) {
  return (
    <section className="space-y-4">
      <Card>
        <CardHeader title="Runtime configuration" />
        <p className="text-sm text-muted">Configure the database connection string, storage paths, and dry-run behavior with environment variables in Docker Compose. Torrent client, SFTP seedbox access, and other integrations are configured in the tabs above.</p>
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <button
            onClick={onSyncNow}
            disabled={syncingJobId !== null}
            className="flex items-center gap-2 rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg disabled:cursor-wait disabled:opacity-60"
          >
            <RefreshCw size={16} className={syncingJobId !== null ? "animate-spin" : undefined} />
            {syncingJobId !== null ? "Syncing..." : "Sync now"}
          </button>
          {syncMessage && <p className="text-sm text-muted">{syncMessage}</p>}
        </div>
      </Card>
      <Card>
        <CardHeader title="Auto download" />
        <div className="grid gap-2 text-sm text-ink md:grid-cols-2">
          <p>Enabled: {String(transferSettings?.enabled ?? false)}</p>
          <p>SFTP configured: {String(transferSettings?.sftpSeedbox.configured ?? false)}</p>
          <p>Host: {transferSettings?.sftpSeedbox.host || "not set"}</p>
          <p>Local paths: {String(transferSettings?.localMediaPaths.length ?? 0)}</p>
        </div>
      </Card>
      <Card>
        <CardHeader title="Job history" />
        {jobs.length ? (
          <div className="space-y-2 text-sm text-ink">
            {jobs.slice(0, 10).map((job) => (
              <p key={job.id} className="rounded-md bg-surfaceMuted px-3 py-2">
                {job.jobType} - {job.status} - {new Date(job.startedAt).toLocaleString()}{job.errorMessage ? ` - ${job.errorMessage}` : ""}
              </p>
            ))}
          </div>
        ) : (
          <EmptyState icon={ClipboardList} message="No jobs recorded yet." />
        )}
      </Card>
    </section>
  );
}
