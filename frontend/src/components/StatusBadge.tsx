type Tone = "muted" | "accent" | "success" | "warn" | "danger";

const toneByStatus: Record<string, Tone> = {
  Queued: "muted",
  PendingReview: "muted",
  Unknown: "muted",
  Cancelled: "muted",
  Deleted: "muted",
  Running: "accent",
  Downloading: "accent",
  Completed: "success",
  Complete: "success",
  Succeeded: "success",
  Seeding: "success",
  Approved: "success",
  Skipped: "warn",
  Paused: "warn",
  Uncertain: "warn",
  Likely: "accent",
  Certain: "success",
  Failed: "danger",
  Error: "danger",
  Rejected: "danger"
};

const toneClasses: Record<Tone, string> = {
  muted: "bg-surfaceMuted text-muted border-line",
  accent: "bg-accentMuted text-accent border-accent/30",
  success: "bg-successMuted text-success border-success/30",
  warn: "bg-warnMuted text-warn border-warn/30",
  danger: "bg-dangerMuted text-danger border-danger/30"
};

export function getStatusTone(status: string): Tone {
  return toneByStatus[status] ?? "muted";
}

export function StatusBadge({ status }: { status: string }) {
  const tone = getStatusTone(status);
  const pulsing = tone === "accent";

  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium ${toneClasses[tone]}`}>
      <span className={`h-1.5 w-1.5 rounded-full bg-current ${pulsing ? "animate-pulse" : ""}`} />
      {status}
    </span>
  );
}
