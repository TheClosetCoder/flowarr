import React from "react";
import { postJson } from "../api";
import { formatBytes } from "../format";
import { Table } from "../components/Table";
import type { StorageLocation } from "../types";

export function Storage({ storage, setStorage }: { storage: StorageLocation[]; setStorage: (items: StorageLocation[]) => void }) {
  const [message, setMessage] = React.useState("");

  async function scanNow() {
    setMessage("Scanning...");
    const updated = await postJson<StorageLocation[]>("/api/storage/scan");
    setStorage(updated);
    setMessage("Storage scan complete.");
  }

  return (
    <section className="space-y-4">
      <div className="flex items-center gap-3">
        <button onClick={scanNow} className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg">Scan now</button>
        <p className="text-sm text-muted">{message}</p>
      </div>
      <Table
        rows={storage.map((item) => [
          item.name,
          String(item.kind),
          item.pathOrEndpoint,
          formatBytes(item.usedBytes),
          formatBytes(item.freeBytes),
          new Date(item.lastScannedAt).toLocaleString()
        ])}
        headers={["Name", "Kind", "Path/API", "Used", "Free", "Last scanned"]}
      />
    </section>
  );
}
