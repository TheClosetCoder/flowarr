import React from "react";
import { fetchJson, postJson } from "../api";
import { formatBytes } from "../format";
import { Card } from "../components/Card";
import { SectionHeader } from "../components/SectionHeader";
import { Table } from "../components/Table";
import { EmptyState } from "../components/EmptyState";
import type { JobRun, StorageLocation } from "../types";

function toRows(items: StorageLocation[]) {
  return items.map((item) => [
    item.name,
    item.pathOrEndpoint,
    formatBytes(item.usedBytes),
    formatBytes(item.freeBytes),
    new Date(item.lastScannedAt).toLocaleString()
  ]);
}

function StorageGroup({ title, description, items }: { title: string; description: string; items: StorageLocation[] }) {
  return (
    <Card>
      <SectionHeader title={title} description={description} />
      <div className="mt-3">
        {items.length ? (
          <Table rows={toRows(items)} headers={["Name", "Path/API", "Used", "Free", "Last scanned"]} />
        ) : (
          <EmptyState message="Nothing in this group yet." />
        )}
      </div>
    </Card>
  );
}

export function Storage({ storage, setStorage }: { storage: StorageLocation[]; setStorage: (items: StorageLocation[]) => void }) {
  const [message, setMessage] = React.useState("");
  const [scanningJobId, setScanningJobId] = React.useState<string | null>(null);

  async function scanNow() {
    try {
      setMessage("Starting scan...");
      const { jobId } = await postJson<{ jobId: string }>("/api/storage/scan");
      setScanningJobId(jobId);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Failed to start storage scan.");
    }
  }

  React.useEffect(() => {
    if (!scanningJobId) {
      return;
    }

    const timer = window.setInterval(() => {
      void (async () => {
        try {
          const runs = await fetchJson<JobRun[]>("/api/jobs");
          const run = runs.find((item) => item.id === scanningJobId);
          if (!run || run.status === "Running") {
            setMessage(run?.progress || "Scanning...");
            return;
          }

          window.clearInterval(timer);
          setScanningJobId(null);

          if (run.status === "Succeeded") {
            setStorage(await fetchJson<StorageLocation[]>("/api/storage"));
            setMessage("Storage scan complete.");
          } else {
            setMessage(run.errorMessage || `Scan ${run.status.toLowerCase()}.`);
          }
        } catch (error) {
          window.clearInterval(timer);
          setScanningJobId(null);
          setMessage(error instanceof Error ? error.message : "Failed to check scan progress.");
        }
      })();
    }, 1500);

    return () => window.clearInterval(timer);
  }, [scanningJobId, setStorage]);

  const isKind = (item: StorageLocation, kind: string) => String(item.kind) === kind;
  const isCategory = (item: StorageLocation, category: string) => String(item.category) === category;

  const localRules = storage.filter((item) => isKind(item, "Local") && isCategory(item, "Rule"));
  const localMonitored = storage.filter((item) => isKind(item, "Local") && isCategory(item, "Monitored"));
  const seedboxRules = storage.filter((item) => isKind(item, "Seedbox") && isCategory(item, "Rule"));
  const seedboxMonitored = storage.filter((item) => isKind(item, "Seedbox") && isCategory(item, "Monitored"));

  return (
    <section className="space-y-4">
      <div className="flex items-center gap-3">
        <button
          onClick={() => void scanNow()}
          disabled={scanningJobId !== null}
          className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg disabled:cursor-wait disabled:opacity-60"
        >
          {scanningJobId !== null ? "Scanning..." : "Scan now"}
        </button>
        <p className="text-sm text-muted">{message}</p>
      </div>

      <StorageGroup
        title="Local — Download rules"
        description="Local folders your Download folder rules deliver files to."
        items={localRules}
      />
      <StorageGroup
        title="Local — Monitored folders"
        description="Local folders you've added to watch for free space, with no rule attached (Local media paths)."
        items={localMonitored}
      />
      <StorageGroup
        title="Seedbox — Download rules"
        description="Remote seedbox folders your Download folder rules pull files from."
        items={seedboxRules}
      />
      <StorageGroup
        title="Seedbox — Monitored folders"
        description="Remote seedbox folders you've added to watch for free space (Seedbox roots)."
        items={seedboxMonitored}
      />
    </section>
  );
}
