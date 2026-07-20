import type { RemoteTransfer } from "./types";

export function formatBytes(value: number) {
  if (!value) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${(value / 1024 ** index).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

export function getTransferProgress(transfer: { status: string; bytesTransferred: number; totalBytes: number }) {
  if (transfer.totalBytes <= 0) {
    return transfer.status === "Completed" ? 1 : 0;
  }

  return Math.min(1, transfer.bytesTransferred / transfer.totalBytes);
}

export function formatRelativeTime(value: string) {
  const date = new Date(value);
  const diffMs = Date.now() - date.getTime();
  const diffSeconds = Math.round(diffMs / 1000);

  if (diffSeconds < 60) return "just now";

  const diffMinutes = Math.round(diffSeconds / 60);
  if (diffMinutes < 60) return `${diffMinutes}m ago`;

  const diffHours = Math.round(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours}h ago`;

  const diffDays = Math.round(diffHours / 24);
  if (diffDays < 30) return `${diffDays}d ago`;

  return date.toLocaleDateString();
}

export function isTransferActive(transfer: Pick<RemoteTransfer, "status">) {
  return transfer.status === "Queued" || transfer.status === "Running";
}

export function getDisplayTitle(remotePath: string, localPath: string) {
  const remoteName = remotePath.replace(/\/+$/, "").split("/").pop();
  if (remoteName) return remoteName;

  const localName = localPath.replace(/[/\\]+$/, "").split(/[/\\]/).pop();
  return localName || remotePath;
}

// Extracts a season (and episode, if present) label from a torrent/file/folder name so it can be
// shown as its own badge - the title text alone is often truncated in the UI, which can hide the
// season/episode part of a long name (it's rarely at the very start of the string).
export function extractSeasonLabel(...values: string[]): string | null {
  const text = values.join(" ");

  const seasonEpisode = text.match(/[Ss](\d{1,2})[Ee](\d{1,3})/);
  if (seasonEpisode) {
    return `S${seasonEpisode[1].padStart(2, "0")}E${seasonEpisode[2].padStart(2, "0")}`;
  }

  const xForm = text.match(/\b(\d{1,2})x(\d{1,3})\b/);
  if (xForm) {
    return `S${xForm[1].padStart(2, "0")}E${xForm[2].padStart(2, "0")}`;
  }

  const seasonWord = text.match(/season[\s._-]*(\d{1,2})/i);
  if (seasonWord) {
    return `Season ${Number(seasonWord[1])}`;
  }

  const seasonOnly = text.match(/\bS(\d{1,2})\b/);
  if (seasonOnly) {
    return `S${seasonOnly[1].padStart(2, "0")}`;
  }

  return null;
}
