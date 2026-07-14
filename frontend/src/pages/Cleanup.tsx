import React from "react";
import { ChevronDown, Copy, Trash2 } from "lucide-react";
import { fetchJson, postJson, putJson } from "../api";
import { formatBytes } from "../format";
import { Card, CardHeader } from "../components/Card";
import { Input } from "../components/Input";
import { StatusBadge } from "../components/StatusBadge";
import { EmptyState } from "../components/EmptyState";
import type { CleanupCandidateSummary, CleanupSettings, JobRun } from "../types";

function basename(path: string) {
  return path.replace(/[/\\]+$/, "").split(/[/\\]/).pop() || path;
}

function groupByDuplicateKey(items: CleanupCandidateSummary[]) {
  const groups = new Map<string, { keptPath?: string; items: CleanupCandidateSummary[] }>();
  for (const item of items) {
    const key = item.duplicateGroupKey ?? item.id;
    const group = groups.get(key);
    if (group) {
      group.items.push(item);
    } else {
      groups.set(key, { keptPath: item.keptPath, items: [item] });
    }
  }
  return Array.from(groups.values());
}

function groupStaleItems(items: CleanupCandidateSummary[]) {
  const movies: CleanupCandidateSummary[] = [];
  const seriesMap = new Map<string, Map<string, CleanupCandidateSummary[]>>();

  for (const item of items) {
    if (!item.seriesTitle) {
      movies.push(item);
      continue;
    }
    const seasonKey = typeof item.seasonNumber === "number" ? String(item.seasonNumber) : "Unknown season";
    const seasons = seriesMap.get(item.seriesTitle) ?? new Map<string, CleanupCandidateSummary[]>();
    seasons.set(seasonKey, [...(seasons.get(seasonKey) ?? []), item]);
    seriesMap.set(item.seriesTitle, seasons);
  }

  const series = Array.from(seriesMap.entries())
    .map(([title, seasons]) => ({
      title,
      seasons: Array.from(seasons.entries())
        .sort((a, b) => (a[0] === "Unknown season" ? 1 : b[0] === "Unknown season" ? -1 : Number(a[0]) - Number(b[0])))
        .map(([season, seasonItems]) => ({ season, items: seasonItems }))
    }))
    .sort((a, b) => a.title.localeCompare(b.title));

  return { movies, series };
}

function totalBytes(items: CleanupCandidateSummary[]) {
  return items.reduce((sum, item) => sum + item.sizeBytes, 0);
}

export function Cleanup({ candidates, setCandidates }: { candidates: CleanupCandidateSummary[]; setCandidates: (items: CleanupCandidateSummary[]) => void }) {
  const [message, setMessage] = React.useState("");
  const [staleAfterDays, setStaleAfterDays] = React.useState(90);
  const [autoGenerateEnabled, setAutoGenerateEnabled] = React.useState(false);
  const [autoGenerateIntervalHours, setAutoGenerateIntervalHours] = React.useState(24);
  const [confirmingId, setConfirmingId] = React.useState<string | null>(null);
  const [confirmText, setConfirmText] = React.useState("");
  const [generatingJobId, setGeneratingJobId] = React.useState<string | null>(null);
  const [showHistory, setShowHistory] = React.useState(false);
  const [historyCandidates, setHistoryCandidates] = React.useState<CleanupCandidateSummary[]>([]);

  React.useEffect(() => {
    void fetchJson<CleanupSettings>("/api/cleanup/settings").then((settings) => {
      setStaleAfterDays(settings.staleAfterDays);
      setAutoGenerateEnabled(settings.autoGenerateEnabled);
      setAutoGenerateIntervalHours(settings.autoGenerateIntervalHours);
    });
  }, []);

  React.useEffect(() => {
    if (!showHistory) {
      return;
    }
    void fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates").then((items) =>
      setHistoryCandidates(items.filter((item) => item.status !== "PendingReview"))
    );
  }, [showHistory]);

  async function saveSettings() {
    await putJson("/api/cleanup/settings", { staleAfterDays, autoGenerateEnabled, autoGenerateIntervalHours });
    setMessage("Cleanup settings saved.");
  }

  async function generate() {
    try {
      setMessage("Starting scan...");
      const { jobId } = await postJson<{ jobId: string }>("/api/cleanup/generate");
      setGeneratingJobId(jobId);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Failed to start generating recommendations.");
    }
  }

  React.useEffect(() => {
    if (!generatingJobId) {
      return;
    }

    const timer = window.setInterval(() => {
      void (async () => {
        try {
          const runs = await fetchJson<JobRun[]>("/api/jobs");
          const run = runs.find((item) => item.id === generatingJobId);
          if (!run || run.status === "Running") {
            setMessage(run?.progress || "Generating recommendations...");
            return;
          }

          window.clearInterval(timer);
          setGeneratingJobId(null);

          if (run.status === "Succeeded") {
            await refreshCandidates();
            setMessage("Scan complete.");
          } else {
            setMessage(run.errorMessage || `Scan ${run.status.toLowerCase()}.`);
          }
        } catch (error) {
          window.clearInterval(timer);
          setGeneratingJobId(null);
          setMessage(error instanceof Error ? error.message : "Failed to check scan progress.");
        }
      })();
    }, 1500);

    return () => window.clearInterval(timer);
  }, [generatingJobId]);

  async function refreshCandidates() {
    setCandidates(await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates?status=PendingReview"));
  }

  async function approveDryRun(id: string) {
    await postJson(`/api/cleanup/candidates/${id}/approve`, { actor: "admin", dryRun: true });
    await refreshCandidates();
    setMessage("Dry-run approval recorded. No file was deleted.");
  }

  async function confirmDelete(item: CleanupCandidateSummary) {
    try {
      await postJson(`/api/cleanup/candidates/${item.id}/approve`, { actor: "admin", dryRun: false, confirmPath: confirmText });
      await refreshCandidates();
      setMessage("File deleted.");
      setConfirmingId(null);
      setConfirmText("");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Delete failed.");
    }
  }

  async function reject(id: string) {
    await postJson(`/api/cleanup/candidates/${id}/reject`, { actor: "admin" });
    await refreshCandidates();
    setMessage("Candidate rejected.");
  }

  function renderActions(item: CleanupCandidateSummary) {
    return (
      <div className="mt-3 flex flex-wrap items-center gap-2 text-sm text-ink">
        <span>{formatBytes(item.sizeBytes)}</span>
        <span className="text-muted">Dry-run: {String(item.dryRun)}</span>
        <button onClick={() => void approveDryRun(item.id)} className="rounded-md border border-line px-3 py-1 hover:bg-surfaceMuted">Approve dry-run</button>
        <button onClick={() => void reject(item.id)} className="rounded-md border border-line px-3 py-1 hover:bg-surfaceMuted">Reject</button>
        {confirmingId === item.id ? (
          <div className="flex flex-wrap items-center gap-2">
            <input
              value={confirmText}
              onChange={(event) => setConfirmText(event.target.value)}
              placeholder="Paste the exact path to confirm"
              className="w-72 rounded-md border border-line bg-surfaceMuted px-2 py-1 text-xs text-ink outline-none focus:border-danger"
            />
            <button
              onClick={() => void confirmDelete(item)}
              disabled={confirmText !== item.path}
              className="rounded-md bg-danger px-3 py-1 text-bg disabled:cursor-not-allowed disabled:opacity-50"
            >
              Confirm delete
            </button>
            <button
              onClick={() => {
                setConfirmingId(null);
                setConfirmText("");
              }}
              className="rounded-md border border-line px-3 py-1 hover:bg-surfaceMuted"
            >
              Cancel
            </button>
          </div>
        ) : (
          <button
            onClick={() => {
              setConfirmingId(item.id);
              setConfirmText("");
            }}
            className="rounded-md border border-danger px-3 py-1 text-danger hover:bg-dangerMuted"
          >
            Delete
          </button>
        )}
      </div>
    );
  }

  function renderCandidateRow(item: CleanupCandidateSummary) {
    return (
      <div key={item.id} className="rounded-md border border-line bg-surfaceMuted/40 p-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="break-all font-medium text-ink">{item.path}</p>
            <p className="mt-1 text-sm text-muted">{item.reason}</p>
            {(typeof item.viewCount === "number" || item.lastViewedAt || item.addedAt) && (
              <p className="mt-1 text-xs text-muted">
                {typeof item.viewCount === "number" && `Watched ${item.viewCount}x. `}
                {item.lastViewedAt && `Last viewed ${new Date(item.lastViewedAt).toLocaleDateString()}. `}
                {item.addedAt && `Added ${new Date(item.addedAt).toLocaleDateString()}.`}
              </p>
            )}
          </div>
          <StatusBadge status={item.confidence} />
        </div>
        {renderActions(item)}
      </div>
    );
  }

  const duplicates = candidates.filter((item) => item.reasonType === "Duplicate");
  const stale = candidates.filter((item) => item.reasonType !== "Duplicate");
  const duplicateGroups = groupByDuplicateKey(duplicates);
  const { movies, series } = groupStaleItems(stale);

  return (
    <section className="space-y-4">
      <Card>
        <div className="flex flex-wrap items-end gap-3">
          <Input label="Consider stale after (days)" value={String(staleAfterDays)} onChange={(value) => setStaleAfterDays(Number(value) || 0)} />
          <label className="flex items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={autoGenerateEnabled} onChange={(event) => setAutoGenerateEnabled(event.target.checked)} />
            Auto-generate
          </label>
          <Input
            label="Every (hours)"
            value={String(autoGenerateIntervalHours)}
            onChange={(value) => setAutoGenerateIntervalHours(Number(value) || 0)}
          />
          <button onClick={saveSettings} className="rounded-md border border-line px-3 py-2 text-sm text-ink hover:bg-surfaceMuted">Save</button>
          <button
            onClick={() => void generate()}
            disabled={generatingJobId !== null}
            className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg disabled:cursor-wait disabled:opacity-60"
          >
            {generatingJobId !== null ? "Generating..." : "Generate recommendations"}
          </button>
        </div>
        {message && <p className="mt-2 text-sm text-muted">{message}</p>}
      </Card>

      {candidates.length ? (
        <>
          <Card>
            <CardHeader
              title="Possible duplicates"
              description={`${duplicates.length} file(s), ${formatBytes(totalBytes(duplicates))} reclaimable`}
              action={<Copy size={18} className="text-muted" />}
            />
            {duplicateGroups.length ? (
              <div className="space-y-3">
                {duplicateGroups.map((group) => (
                  <div key={group.items[0].duplicateGroupKey ?? group.items[0].id} className="rounded-md border border-line p-3">
                    {group.keptPath && (
                      <p className="mb-2 text-xs text-muted">
                        Keeping <span className="font-medium text-ink">{basename(group.keptPath)}</span>
                      </p>
                    )}
                    <div className="space-y-2">{group.items.map(renderCandidateRow)}</div>
                  </div>
                ))}
              </div>
            ) : (
              <EmptyState icon={Copy} message="No duplicates flagged." />
            )}
          </Card>

          <Card>
            <CardHeader title="Stale / unwatched" description={`${stale.length} file(s), ${formatBytes(totalBytes(stale))} reclaimable`} />
            {series.length || movies.length ? (
              <div className="space-y-2">
                {series.map((seriesGroup) => (
                  <details key={seriesGroup.title} className="rounded-md border border-line p-3">
                    <summary className="flex cursor-pointer items-center justify-between gap-2 text-sm font-medium text-ink">
                      <span className="flex items-center gap-2">
                        <ChevronDown size={14} className="text-muted" />
                        {seriesGroup.title}
                      </span>
                      <span className="text-xs font-normal text-muted">
                        {seriesGroup.seasons.reduce((sum, season) => sum + season.items.length, 0)} file(s), {formatBytes(totalBytes(seriesGroup.seasons.flatMap((season) => season.items)))}
                      </span>
                    </summary>
                    <div className="mt-3 space-y-2 pl-2">
                      {seriesGroup.seasons.map((season) => (
                        <details key={season.season} className="rounded-md border border-line/60 p-2">
                          <summary className="flex cursor-pointer items-center justify-between gap-2 text-sm text-ink">
                            <span>{season.season === "Unknown season" ? season.season : `Season ${season.season}`}</span>
                            <span className="text-xs text-muted">{season.items.length} file(s), {formatBytes(totalBytes(season.items))}</span>
                          </summary>
                          <div className="mt-2 space-y-2">{season.items.map(renderCandidateRow)}</div>
                        </details>
                      ))}
                    </div>
                  </details>
                ))}
                {movies.length > 0 && <div className="space-y-2">{movies.map(renderCandidateRow)}</div>}
              </div>
            ) : (
              <EmptyState icon={Trash2} message="Nothing stale right now." />
            )}
          </Card>
        </>
      ) : (
        <EmptyState icon={Trash2} message="No cleanup candidates yet." />
      )}

      <Card>
        <CardHeader
          title="Review history"
          description="Previously approved, rejected, or deleted candidates."
          action={
            <button onClick={() => setShowHistory((value) => !value)} className="rounded-md border border-line px-3 py-1 text-sm text-ink hover:bg-surfaceMuted">
              {showHistory ? "Hide" : "Show"}
            </button>
          }
        />
        {showHistory && (
          historyCandidates.length ? (
            <div className="space-y-2">
              {historyCandidates.map((item) => (
                <div key={item.id} className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-line bg-surfaceMuted/40 p-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm text-ink">{item.path}</p>
                    <p className="text-xs text-muted">{item.reason}</p>
                  </div>
                  <StatusBadge status={item.status ?? "Unknown"} />
                </div>
              ))}
            </div>
          ) : (
            <EmptyState icon={Trash2} message="No reviewed candidates yet." />
          )
        )}
      </Card>
    </section>
  );
}
