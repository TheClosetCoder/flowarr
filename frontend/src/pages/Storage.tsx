import React from "react";
import { postJson } from "../api";
import { formatBytes } from "../format";
import { Card } from "../components/Card";
import { SectionHeader } from "../components/SectionHeader";
import { Table } from "../components/Table";
import { EmptyState } from "../components/EmptyState";
import type { StorageLocation } from "../types";

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

  async function scanNow() {
    setMessage("Scanning...");
    const updated = await postJson<StorageLocation[]>("/api/storage/scan");
    setStorage(updated);
    setMessage("Storage scan complete.");
  }

  const isKind = (item: StorageLocation, kind: string) => String(item.kind) === kind;
  const isCategory = (item: StorageLocation, category: string) => String(item.category) === category;

  const localRules = storage.filter((item) => isKind(item, "Local") && isCategory(item, "Rule"));
  const localMonitored = storage.filter((item) => isKind(item, "Local") && isCategory(item, "Monitored"));
  const seedboxRules = storage.filter((item) => isKind(item, "Seedbox") && isCategory(item, "Rule"));
  const seedboxMonitored = storage.filter((item) => isKind(item, "Seedbox") && isCategory(item, "Monitored"));

  return (
    <section className="space-y-4">
      <div className="flex items-center gap-3">
        <button onClick={scanNow} className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg">Scan now</button>
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
