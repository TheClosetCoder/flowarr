import React from "react";
import { Film } from "lucide-react";
import { StatusBadge } from "./StatusBadge";
import { ProgressBar } from "./ProgressBar";
import { formatBytes, getTransferProgress } from "../format";

export function ActivityRow({
  title,
  status,
  bytesTransferred,
  totalBytes,
  message,
  icon: Icon = Film
}: {
  title: string;
  status: string;
  bytesTransferred: number;
  totalBytes: number;
  message?: string;
  icon?: React.ComponentType<{ size?: number }>;
}) {
  const progress = getTransferProgress({ status, bytesTransferred, totalBytes });

  return (
    <div className="flex items-center gap-3 rounded-md border border-line bg-surfaceMuted/40 p-3">
      <div className="grid h-10 w-10 flex-none place-items-center rounded-md bg-accentMuted text-accent">
        <Icon size={18} />
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="min-w-0 flex-1 truncate text-sm font-medium text-ink">{title}</p>
          <StatusBadge status={status} />
        </div>
        <div className="mt-2">
          <ProgressBar value={progress} />
        </div>
        <div className="mt-1 flex flex-wrap justify-between gap-2 text-xs text-muted">
          <span>{formatBytes(bytesTransferred)} / {formatBytes(totalBytes)}</span>
          {message && <span className="truncate">{message}</span>}
        </div>
      </div>
    </div>
  );
}
