import { Info, LogOut } from "lucide-react";

export function TopBar({ activePage, onLogout }: { activePage: string; onLogout: () => void }) {
  return (
    <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div className="flex items-center gap-2">
        <h2 className="text-2xl font-semibold text-ink">{activePage}</h2>
        <span className="group relative inline-flex">
          <Info size={16} className="text-muted" />
          <span className="pointer-events-none absolute left-0 top-7 z-20 hidden w-72 rounded-md border border-line bg-surfaceMuted px-3 py-2 text-xs font-normal text-ink shadow-xl group-hover:block">
            Dry-run cleanup is enabled by default. Deletion requires manual approval.
          </span>
        </span>
      </div>
      <button
        onClick={onLogout}
        className="inline-flex items-center gap-2 rounded-md border border-line bg-surface px-3 py-2 text-sm text-ink hover:bg-surfaceMuted"
      >
        <LogOut size={16} />
        Logout
      </button>
    </div>
  );
}
