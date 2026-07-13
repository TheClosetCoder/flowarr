import React from "react";
import { Trash2 } from "lucide-react";
import { fetchJson, postJson } from "../api";
import { formatBytes } from "../format";
import { Card } from "../components/Card";
import { StatusBadge } from "../components/StatusBadge";
import { EmptyState } from "../components/EmptyState";
import type { CleanupCandidateSummary } from "../types";

export function Cleanup({ candidates, setCandidates }: { candidates: CleanupCandidateSummary[]; setCandidates: (items: CleanupCandidateSummary[]) => void }) {
  const [message, setMessage] = React.useState("");

  async function generate() {
    const generated = await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/generate");
    setCandidates(generated);
  }

  async function approveDryRun(id: string) {
    await postJson(`/api/cleanup/candidates/${id}/approve`, { actor: "admin", dryRun: true });
    setCandidates(await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates"));
    setMessage("Dry-run approval recorded.");
  }

  async function reject(id: string) {
    await postJson(`/api/cleanup/candidates/${id}/reject`, { actor: "admin" });
    setCandidates(await fetchJson<CleanupCandidateSummary[]>("/api/cleanup/candidates"));
    setMessage("Candidate rejected.");
  }

  return (
    <section className="space-y-4">
      <div className="flex items-center gap-3">
        <button onClick={generate} className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg">Generate recommendations</button>
        {message && <p className="text-sm text-muted">{message}</p>}
      </div>
      {candidates.length ? (
        <div className="space-y-3">
          {candidates.map((item) => (
            <Card key={item.id}>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="break-all font-medium text-ink">{item.path}</p>
                  <p className="mt-1 text-sm text-muted">{item.reason}</p>
                </div>
                <StatusBadge status={item.confidence} />
              </div>
              <div className="mt-4 flex flex-wrap items-center gap-2 text-sm text-ink">
                <span>{formatBytes(item.sizeBytes)}</span>
                <span className="text-muted">Dry-run: {String(item.dryRun)}</span>
                <button onClick={() => approveDryRun(item.id)} className="rounded-md border border-line px-3 py-1 hover:bg-surfaceMuted">Approve dry-run</button>
                <button onClick={() => reject(item.id)} className="rounded-md border border-line px-3 py-1 hover:bg-surfaceMuted">Reject</button>
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
