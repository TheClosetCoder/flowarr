import React from "react";
import { Trash2 } from "lucide-react";
import { fetchJson, postJson, putJson } from "../api";
import { formatBytes } from "../format";
import { Card } from "../components/Card";
import { Input } from "../components/Input";
import { StatusBadge } from "../components/StatusBadge";
import { EmptyState } from "../components/EmptyState";
import type { CleanupCandidateSummary, CleanupSettings, JobRun } from "../types";

export function Cleanup({ candidates, setCandidates }: { candidates: CleanupCandidateSummary[]; setCandidates: (items: CleanupCandidateSummary[]) => void }) {
  const [message, setMessage] = React.useState("");
  const [staleAfterDays, setStaleAfterDays] = React.useState(90);
  const [confirmingId, setConfirmingId] = React.useState<string | null>(null);
  const [confirmText, setConfirmText] = React.useState("");
  const [generatingJobId, setGeneratingJobId] = React.useState<string | null>(null);

  React.useEffect(() => {
    void fetchJson<CleanupSettings>("/api/cleanup/settings").then((settings) => setStaleAfterDays(settings.staleAfterDays));
  }, []);

  async function saveStaleAfterDays() {
    await putJson("/api/cleanup/settings", { staleAfterDays });
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
    setCandidates(await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates"));
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

  return (
    <section className="space-y-4">
      <Card>
        <div className="flex flex-wrap items-end gap-3">
          <Input label="Consider stale after (days)" value={String(staleAfterDays)} onChange={(value) => setStaleAfterDays(Number(value) || 0)} />
          <button onClick={saveStaleAfterDays} className="rounded-md border border-line px-3 py-2 text-sm text-ink hover:bg-surfaceMuted">Save</button>
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
        <div className="space-y-3">
          {candidates.map((item) => (
            <Card key={item.id}>
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
              <div className="mt-4 flex flex-wrap items-center gap-2 text-sm text-ink">
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
            </Card>
          ))}
        </div>
      ) : (
        <EmptyState icon={Trash2} message="No cleanup candidates yet." />
      )}
    </section>
  );
}
