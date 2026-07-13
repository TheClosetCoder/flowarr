import { formatBytes } from "../format";
import { Table } from "../components/Table";
import type { DownloadItem } from "../types";

export function Downloads({ downloads }: { downloads: DownloadItem[] }) {
  return (
    <Table
      rows={downloads.map((item) => [item.name, item.client, item.status, `${Math.round(item.progress * 100)}%`, formatBytes(item.sizeBytes), item.remotePath ?? ""])}
      headers={["Name", "Client", "Status", "Progress", "Size", "Remote path"]}
    />
  );
}
