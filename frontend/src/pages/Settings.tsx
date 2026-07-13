import { Card, CardHeader } from "../components/Card";
import { EmptyState } from "../components/EmptyState";
import { ClipboardList } from "lucide-react";
import type { JobRun, TransferSettings } from "../types";

export function SettingsPanel({ transferSettings, jobs }: { transferSettings: TransferSettings | null; jobs: JobRun[] }) {
  return (
    <section className="space-y-4">
      <Card>
        <CardHeader title="Runtime configuration" />
        <p className="text-sm text-muted">Configure PostgreSQL, torrent client, SFTP seedbox access, storage paths, and dry-run behavior with environment variables in Docker Compose.</p>
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
