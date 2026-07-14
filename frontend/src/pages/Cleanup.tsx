import React from "react";
import { ChevronDown, Copy, Trash2 } from "lucide-react";
import { fetchJson, postJson, putJson } from "../api";
import { formatBytes } from "../format";
import { Card, CardHeader } from "../components/Card";
import { Input } from "../components/Input";
import { StatusBadge } from "../components/StatusBadge";
import { EmptyState } from "../components/EmptyState";
import type { BulkActionResult, CleanupCandidateSummary, CleanupSettings, JobRun } from "../types";

type SeriesSeasonTree = {
  movies: CleanupCandidateSummary[];
  series: { title: string; seasons: { season: string; items: CleanupCandidateSummary[] }[] }[];
};

function groupBySeriesSeason(items: CleanupCandidateSummary[]): SeriesSeasonTree {
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

function totalBytes(items: CleanupCandidateSummary[]) {
  return items.reduce((sum, item) => sum + item.sizeBytes, 0);
}

function idsOf(items: CleanupCandidateSummary[]) {
  return items.map((item) => item.id);
}

type Tab = "duplicates" | "stale" | "history";

export function Cleanup({ candidates, setCandidates }: { candidates: CleanupCandidateSummary[]; setCandidates: (items: CleanupCandidateSummary[]) => void }) {
  const [message, setMessage] = React.useState("");
  const [staleAfterDays, setStaleAfterDays] = React.useState(90);
  const [autoGenerateEnabled, setAutoGenerateEnabled] = React.useState(false);
  const [autoGenerateIntervalHours, setAutoGenerateIntervalHours] = React.useState(24);
  const [confirmingId, setConfirmingId] = React.useState<string | null>(null);
  const [confirmText, setConfirmText] = React.useState("");
  const [copiedId, setCopiedId] = React.useState<string | null>(null);
  const [rowErrors, setRowErrors] = React.useState<Record<string, string>>({});
  const [generatingJobId, setGeneratingJobId] = React.useState<string | null>(null);
  const [activeTab, setActiveTab] = React.useState<Tab>("duplicates");
  const [historyCandidates, setHistoryCandidates] = React.useState<CleanupCandidateSummary[]>([]);
  const [selectedIds, setSelectedIds] = React.useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = React.useState(false);
  const [bulkMessage, setBulkMessage] = React.useState("");
  const [bulkConfirming, setBulkConfirming] = React.useState(false);
  const [bulkConfirmPhrase, setBulkConfirmPhrase] = React.useState("");

  React.useEffect(() => {
    void fetchJson<CleanupSettings>("/api/cleanup/settings").then((settings) => {
      setStaleAfterDays(settings.staleAfterDays);
      setAutoGenerateEnabled(settings.autoGenerateEnabled);
      setAutoGenerateIntervalHours(settings.autoGenerateIntervalHours);
    });
  }, []);

  React.useEffect(() => {
    if (activeTab !== "history") {
      return;
    }
    void fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates").then((items) =>
      setHistoryCandidates(items.filter((item) => item.status !== "PendingReview"))
    );
  }, [activeTab]);

  function changeTab(tab: Tab) {
    setActiveTab(tab);
    setSelectedIds(new Set());
    setBulkConfirming(false);
    setBulkMessage("");
  }

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

  function clearRowError(id: string) {
    setRowErrors((current) => {
      if (!(id in current)) return current;
      const next = { ...current };
      delete next[id];
      return next;
    });
  }

  function setRowError(id: string, error: unknown, fallback: string) {
    setRowErrors((current) => ({ ...current, [id]: error instanceof Error ? error.message : fallback }));
  }

  async function approveDryRun(id: string) {
    try {
      await postJson(`/api/cleanup/candidates/${id}/approve`, { actor: "admin", dryRun: true });
      clearRowError(id);
      await refreshCandidates();
      setMessage("Dry-run approval recorded. No file was deleted.");
    } catch (error) {
      setRowError(id, error, "Approve failed.");
    }
  }

  async function confirmDelete(item: CleanupCandidateSummary) {
    try {
      await postJson(`/api/cleanup/candidates/${item.id}/approve`, { actor: "admin", dryRun: false, confirmPath: confirmText });
      clearRowError(item.id);
      await refreshCandidates();
      setMessage("File deleted.");
      setConfirmingId(null);
      setConfirmText("");
    } catch (error) {
      setRowError(item.id, error, "Delete failed.");
    }
  }

  async function reject(id: string) {
    try {
      await postJson(`/api/cleanup/candidates/${id}/reject`, { actor: "admin" });
      clearRowError(id);
      await refreshCandidates();
      setMessage("Candidate rejected.");
    } catch (error) {
      setRowError(id, error, "Reject failed.");
    }
  }

  function toggleId(id: string) {
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  function toggleGroup(ids: string[]) {
    setSelectedIds((current) => {
      const next = new Set(current);
      const allSelected = ids.length > 0 && ids.every((id) => next.has(id));
      for (const id of ids) {
        if (allSelected) {
          next.delete(id);
        } else {
          next.add(id);
        }
      }
      return next;
    });
  }

  function summarizeBulkResult(results: BulkActionResult[], verb: string) {
    const failed = results.filter((result) => !result.ok);
    if (failed.length === 0) {
      setBulkMessage(`${verb} ${results.length} file(s).`);
      return;
    }
    const extra = failed.length > 1 ? ` (+${failed.length - 1} more)` : "";
    setBulkMessage(`${verb} ${results.length - failed.length} of ${results.length} file(s). ${failed.length} failed: ${failed[0].error ?? "unknown error"}${extra}`);
  }

  async function bulkApproveDryRun() {
    const ids = Array.from(selectedIds);
    if (!ids.length) return;
    setBulkBusy(true);
    setBulkMessage("");
    try {
      const results = await postJson<BulkActionResult[]>("/api/cleanup/candidates/bulk-approve", { actor: "admin", dryRun: true, candidateIds: ids });
      summarizeBulkResult(results, "Approved");
      setSelectedIds(new Set());
      await refreshCandidates();
    } catch (error) {
      setBulkMessage(error instanceof Error ? error.message : "Bulk approve failed.");
    } finally {
      setBulkBusy(false);
    }
  }

  async function bulkReject() {
    const ids = Array.from(selectedIds);
    if (!ids.length) return;
    setBulkBusy(true);
    setBulkMessage("");
    try {
      const results = await postJson<BulkActionResult[]>("/api/cleanup/candidates/bulk-reject", { actor: "admin", candidateIds: ids });
      summarizeBulkResult(results, "Rejected");
      setSelectedIds(new Set());
      await refreshCandidates();
    } catch (error) {
      setBulkMessage(error instanceof Error ? error.message : "Bulk reject failed.");
    } finally {
      setBulkBusy(false);
    }
  }

  async function bulkDelete() {
    const ids = Array.from(selectedIds);
    if (!ids.length) return;
    setBulkBusy(true);
    setBulkMessage("");
    try {
      const results = await postJson<BulkActionResult[]>("/api/cleanup/candidates/bulk-approve", { actor: "admin", dryRun: false, candidateIds: ids });
      summarizeBulkResult(results, "Deleted");
      setSelectedIds(new Set());
      setBulkConfirming(false);
      setBulkConfirmPhrase("");
      await refreshCandidates();
    } catch (error) {
      setBulkMessage(error instanceof Error ? error.message : "Bulk delete failed.");
    } finally {
      setBulkBusy(false);
    }
  }

  function renderActions(item: CleanupCandidateSummary) {
    const rowError = rowErrors[item.id];
    return (
      <div className="mt-3 space-y-2">
        <div className="flex flex-wrap items-center gap-2 text-sm text-ink">
          <span>{formatBytes(item.sizeBytes)}</span>
          <span className="text-muted">Dry-run: {String(item.dryRun)}</span>
          <button onClick={() => void approveDryRun(item.id)} className="rounded-md border border-line px-3 py-1 hover:bg-surfaceMuted">Approve dry-run</button>
          <button onClick={() => void reject(item.id)} className="rounded-md border border-line px-3 py-1 hover:bg-surfaceMuted">Reject</button>
          {confirmingId !== item.id && (
            <button
              onClick={() => {
                setConfirmingId(item.id);
                setConfirmText("");
                setCopiedId(null);
                clearRowError(item.id);
              }}
              className="rounded-md border border-danger px-3 py-1 text-danger hover:bg-dangerMuted"
            >
              Delete
            </button>
          )}
        </div>
        {rowError && confirmingId !== item.id && (
          <p className="rounded-md border border-danger/50 bg-dangerMuted px-3 py-2 text-sm text-danger">{rowError}</p>
        )}
        {confirmingId === item.id && (
          <div className="rounded-md border border-danger/50 bg-dangerMuted p-3">
            <p className="text-xs font-medium uppercase tracking-wide text-danger">This exact location will be deleted</p>
            <p className="mt-1 break-all font-mono text-sm text-ink">{item.path}</p>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <input
                value={confirmText}
                onChange={(event) => setConfirmText(event.target.value)}
                placeholder="Paste the exact path to confirm"
                className="w-72 rounded-md border border-line bg-surfaceMuted px-2 py-1 text-xs text-ink outline-none focus:border-danger"
              />
              <button
                onClick={() => {
                  void navigator.clipboard.writeText(item.path);
                  setCopiedId(item.id);
                  window.setTimeout(() => setCopiedId((current) => (current === item.id ? null : current)), 1500);
                }}
                className="rounded-md border border-line px-3 py-1 text-xs text-ink hover:bg-surfaceMuted"
              >
                {copiedId === item.id ? "Copied!" : "Copy path"}
              </button>
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
            {rowError && <p className="mt-2 text-sm text-danger">{rowError}</p>}
          </div>
        )}
      </div>
    );
  }

  function renderCandidateRow(item: CleanupCandidateSummary) {
    return (
      <div key={item.id} className="rounded-md border border-line bg-surfaceMuted/40 p-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="flex min-w-0 items-start gap-2">
            <input
              type="checkbox"
              checked={selectedIds.has(item.id)}
              onChange={() => toggleId(item.id)}
              className="mt-1 h-4 w-4 flex-none accent-accent"
              title="Select this file"
            />
            <div className="min-w-0">
              {item.reasonType === "Duplicate" && (
                <p className="text-xs font-medium uppercase tracking-wide text-danger">
                  {item.keptPath ? "Extra copy - candidate for deletion" : "Possible duplicate - candidate for deletion"}
                </p>
              )}
              <p className="break-all font-mono text-sm font-medium text-ink">{item.path}</p>
              <p className="mt-1 text-sm text-muted">{item.reason}</p>
              {(typeof item.viewCount === "number" || item.lastViewedAt || item.addedAt) && (
                <p className="mt-1 text-xs text-muted">
                  {typeof item.viewCount === "number" && `Watched ${item.viewCount}x. `}
                  {item.lastViewedAt && `Last viewed ${new Date(item.lastViewedAt).toLocaleDateString()}. `}
                  {item.addedAt && `Added ${new Date(item.addedAt).toLocaleDateString()}.`}
                </p>
              )}
            </div>
          </div>
          <StatusBadge status={item.confidence} />
        </div>
        {renderActions(item)}
      </div>
    );
  }

  function renderDuplicateCluster(group: { keptPath?: string; items: CleanupCandidateSummary[] }) {
    const ids = idsOf(group.items);
    const key = group.items[0]?.duplicateGroupKey ?? group.items[0]?.id;
    return (
      <div key={key} className="rounded-md border border-line p-3">
        <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
          <label className="flex items-center gap-2 text-xs text-muted">
            <input type="checkbox" checked={ids.every((id) => selectedIds.has(id))} onChange={() => toggleGroup(ids)} className="h-4 w-4 accent-accent" />
            Select all {ids.length} in this group
          </label>
          <span className="text-xs text-muted">{formatBytes(totalBytes(group.items))}</span>
        </div>
        {group.keptPath && (
          <div className="mb-3 rounded-md border border-success/30 bg-successMuted px-3 py-2">
            <p className="text-xs font-medium uppercase tracking-wide text-success">Keeping this location</p>
            <p className="mt-1 break-all font-mono text-sm text-ink">{group.keptPath}</p>
          </div>
        )}
        <div className="space-y-2">{group.items.map(renderCandidateRow)}</div>
      </div>
    );
  }

  function renderSeriesSeasonTree(tree: SeriesSeasonTree, renderLeaf: (items: CleanupCandidateSummary[]) => React.ReactNode) {
    if (!tree.series.length && !tree.movies.length) {
      return <EmptyState icon={Trash2} message="Nothing here right now." />;
    }

    return (
      <div className="space-y-2">
        {tree.series.map((seriesGroup) => {
          const allSeriesIds = seriesGroup.seasons.flatMap((season) => idsOf(season.items));
          const seriesItems = seriesGroup.seasons.flatMap((season) => season.items);
          return (
            <details key={seriesGroup.title} className="rounded-md border border-line p-3">
              <summary className="flex cursor-pointer items-center justify-between gap-2 text-sm font-medium text-ink">
                <span className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={allSeriesIds.length > 0 && allSeriesIds.every((id) => selectedIds.has(id))}
                    onClick={(event) => event.stopPropagation()}
                    onChange={() => toggleGroup(allSeriesIds)}
                    className="h-4 w-4 accent-accent"
                    title="Select all in this show"
                  />
                  <ChevronDown size={14} className="text-muted" />
                  {seriesGroup.title}
                </span>
                <span className="text-xs font-normal text-muted">
                  {allSeriesIds.length} file(s), {formatBytes(totalBytes(seriesItems))}
                </span>
              </summary>
              <div className="mt-3 space-y-2 pl-2">
                {seriesGroup.seasons.map((season) => {
                  const seasonIds = idsOf(season.items);
                  return (
                    <details key={season.season} className="rounded-md border border-line/60 p-2">
                      <summary className="flex cursor-pointer items-center justify-between gap-2 text-sm text-ink">
                        <span className="flex items-center gap-2">
                          <input
                            type="checkbox"
                            checked={seasonIds.length > 0 && seasonIds.every((id) => selectedIds.has(id))}
                            onClick={(event) => event.stopPropagation()}
                            onChange={() => toggleGroup(seasonIds)}
                            className="h-4 w-4 accent-accent"
                            title="Select all in this season"
                          />
                          {season.season === "Unknown season" ? season.season : `Season ${season.season}`}
                        </span>
                        <span className="text-xs text-muted">{season.items.length} file(s), {formatBytes(totalBytes(season.items))}</span>
                      </summary>
                      <div className="mt-2 space-y-2">{renderLeaf(season.items)}</div>
                    </details>
                  );
                })}
              </div>
            </details>
          );
        })}
        {tree.movies.length > 0 && (
          <div className="rounded-md border border-line p-3">
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted">Movies</p>
            {renderLeaf(tree.movies)}
          </div>
        )}
      </div>
    );
  }

  function renderBulkBar() {
    if (selectedIds.size === 0) {
      return null;
    }
    const selectedItems = candidates.filter((item) => selectedIds.has(item.id));
    return (
      <Card className="border-accent/40">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-sm font-medium text-ink">{selectedIds.size} selected · {formatBytes(totalBytes(selectedItems))}</span>
          <button onClick={() => void bulkApproveDryRun()} disabled={bulkBusy} className="rounded-md border border-line px-3 py-1 text-sm hover:bg-surfaceMuted disabled:opacity-50">Approve dry-run</button>
          <button onClick={() => void bulkReject()} disabled={bulkBusy} className="rounded-md border border-line px-3 py-1 text-sm hover:bg-surfaceMuted disabled:opacity-50">Reject</button>
          <button
            onClick={() => {
              setBulkConfirming(true);
              setBulkConfirmPhrase("");
            }}
            disabled={bulkBusy}
            className="rounded-md border border-danger px-3 py-1 text-sm text-danger hover:bg-dangerMuted disabled:opacity-50"
          >
            Delete...
          </button>
          <button
            onClick={() => {
              setSelectedIds(new Set());
              setBulkConfirming(false);
            }}
            className="rounded-md border border-line px-3 py-1 text-sm hover:bg-surfaceMuted"
          >
            Clear selection
          </button>
        </div>
        {bulkMessage && <p className="mt-2 text-sm text-muted">{bulkMessage}</p>}
        {bulkConfirming && (
          <div className="mt-3 rounded-md border border-danger/50 bg-dangerMuted p-3">
            <p className="text-xs font-medium uppercase tracking-wide text-danger">
              These {selectedItems.length} location(s) will be permanently deleted
            </p>
            <div className="mt-2 max-h-48 space-y-1 overflow-y-auto rounded-md border border-line bg-surface p-2">
              {selectedItems.map((item) => (
                <p key={item.id} className="break-all font-mono text-xs text-ink">{item.path}</p>
              ))}
            </div>
            <p className="mt-2 text-xs text-muted">
              Type <span className="font-mono text-ink">DELETE {selectedItems.length} FILES</span> to confirm.
            </p>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <input
                value={bulkConfirmPhrase}
                onChange={(event) => setBulkConfirmPhrase(event.target.value)}
                placeholder={`DELETE ${selectedItems.length} FILES`}
                className="w-72 rounded-md border border-line bg-surfaceMuted px-2 py-1 text-xs text-ink outline-none focus:border-danger"
              />
              <button
                onClick={() => void bulkDelete()}
                disabled={bulkConfirmPhrase !== `DELETE ${selectedItems.length} FILES` || bulkBusy}
                className="rounded-md bg-danger px-3 py-1 text-sm text-bg disabled:cursor-not-allowed disabled:opacity-50"
              >
                Confirm delete
              </button>
              <button onClick={() => setBulkConfirming(false)} className="rounded-md border border-line px-3 py-1 text-sm hover:bg-surfaceMuted">
                Cancel
              </button>
            </div>
          </div>
        )}
      </Card>
    );
  }

  const duplicates = candidates.filter((item) => item.reasonType === "Duplicate");
  const stale = candidates.filter((item) => item.reasonType !== "Duplicate");
  const duplicateTree = groupBySeriesSeason(duplicates);
  const staleTree = groupBySeriesSeason(stale);

  const tabs: { id: Tab; label: string; count?: number }[] = [
    { id: "duplicates", label: "Duplicates", count: duplicates.length },
    { id: "stale", label: "Stale / unwatched", count: stale.length },
    { id: "history", label: "Review history" }
  ];

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

      <div className="overflow-x-auto rounded-lg border border-line bg-surface p-1">
        <div className="flex min-w-max gap-1">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              onClick={() => changeTab(tab.id)}
              className={`flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium ${
                activeTab === tab.id ? "bg-accentMuted text-accent" : "text-muted hover:bg-surfaceMuted hover:text-ink"
              }`}
            >
              {tab.label}
              {typeof tab.count === "number" && (
                <span className={`rounded-full px-2 py-0.5 text-xs ${activeTab === tab.id ? "bg-accent text-bg" : "bg-surfaceMuted text-muted"}`}>{tab.count}</span>
              )}
            </button>
          ))}
        </div>
      </div>

      {activeTab === "duplicates" && (
        <>
          {renderBulkBar()}
          <Card>
            <CardHeader
              title="Possible duplicates"
              description={`${duplicates.length} file(s), ${formatBytes(totalBytes(duplicates))} reclaimable`}
              action={<Copy size={18} className="text-muted" />}
            />
            {renderSeriesSeasonTree(duplicateTree, (items) => (
              <div className="space-y-3">{groupByDuplicateKey(items).map(renderDuplicateCluster)}</div>
            ))}
          </Card>
        </>
      )}

      {activeTab === "stale" && (
        <>
          {renderBulkBar()}
          <Card>
            <CardHeader title="Stale / unwatched" description={`${stale.length} file(s), ${formatBytes(totalBytes(stale))} reclaimable`} />
            {renderSeriesSeasonTree(staleTree, (items) => <div className="space-y-2">{items.map(renderCandidateRow)}</div>)}
          </Card>
        </>
      )}

      {activeTab === "history" && (
        <Card>
          <CardHeader title="Review history" description="Previously approved, rejected, or deleted candidates." />
          {historyCandidates.length ? (
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
          )}
        </Card>
      )}
    </section>
  );
}
