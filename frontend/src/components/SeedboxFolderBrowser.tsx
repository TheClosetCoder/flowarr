import React from "react";
import { Check, ChevronUp, FolderOpen, X } from "lucide-react";
import { fetchJson } from "../api";
import type { LocalFolderListing } from "../types";

export function SeedboxFolderBrowser({
  initialPath,
  sourceRootKind,
  sourceRootPath,
  onSelect,
  onClose
}: {
  initialPath: string;
  sourceRootKind: "Download" | "Media";
  sourceRootPath: string;
  onSelect: (path: string) => void;
  onClose: () => void;
}) {
  const [listing, setListing] = React.useState<LocalFolderListing | null>(null);
  const [currentPath, setCurrentPath] = React.useState(initialPath);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState("");

  const loadPath = React.useCallback(async (path: string) => {
    setLoading(true);
    setError("");
    try {
      const query = new URLSearchParams({ kind: sourceRootKind });
      if (sourceRootPath) query.set("rootPath", sourceRootPath);
      if (path) query.set("path", path);
      const nextListing = await fetchJson<LocalFolderListing>(`/api/seedbox-folders?${query.toString()}`);
      setListing(nextListing);
      setCurrentPath(nextListing.currentPath || "");
    } catch (err) {
      setListing(null);
      setError(err instanceof Error ? err.message : "Unable to browse seedbox.");
    } finally {
      setLoading(false);
    }
  }, [sourceRootKind, sourceRootPath]);

  React.useEffect(() => {
    void loadPath(initialPath);
  }, [initialPath, loadPath]);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-black/60 px-4 py-6">
      <div className="w-full max-w-2xl overflow-hidden rounded-lg border border-line bg-surface shadow-xl">
        <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-3">
          <div className="min-w-0">
            <h3 className="font-medium text-ink">Browse seedbox folders</h3>
            <p className="truncate text-xs text-muted">{listing?.currentPath || sourceRootPath || `${sourceRootKind} roots`}</p>
          </div>
          <button type="button" onClick={onClose} title="Close" className="grid h-9 w-9 place-items-center rounded-md border border-line text-muted hover:text-ink">
            <X size={17} />
          </button>
        </div>
        <div className="max-h-[60vh] overflow-y-auto p-4">
          {error && <div className="mb-3 rounded-md border border-warn/30 bg-warnMuted px-3 py-2 text-sm text-warn">{error}</div>}
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <button type="button" onClick={() => void loadPath("")} className="rounded-md border border-line px-3 py-2 text-sm text-ink hover:bg-surfaceMuted">Roots</button>
            {listing?.parentPath && (
              <button type="button" onClick={() => void loadPath(listing.parentPath!)} className="inline-flex items-center gap-2 rounded-md border border-line px-3 py-2 text-sm text-ink hover:bg-surfaceMuted">
                <ChevronUp size={16} />
                Up
              </button>
            )}
            {currentPath && (
              <button type="button" onClick={() => onSelect(currentPath)} className="inline-flex items-center gap-2 rounded-md bg-accent px-3 py-2 text-sm font-medium text-bg">
                <Check size={16} />
                Select
              </button>
            )}
          </div>
          {loading ? (
            <p className="text-sm text-muted">Loading folders...</p>
          ) : (
            <div className="space-y-2">
              {(listing?.entries ?? []).length ? (listing?.entries ?? []).map((entry) => (
                <button
                  key={entry.path}
                  type="button"
                  onClick={() => void loadPath(entry.path)}
                  className="flex w-full items-center gap-3 rounded-md border border-line px-3 py-2 text-left text-sm text-ink hover:bg-surfaceMuted"
                >
                  <FolderOpen size={17} className="text-accent" />
                  <span className="min-w-0 flex-1 truncate">{entry.name}</span>
                  <span className="hidden max-w-xs truncate text-xs text-muted sm:block">{entry.path}</span>
                </button>
              )) : (
                <p className="rounded-md bg-surfaceMuted px-3 py-2 text-sm text-muted">No folders found here.</p>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
