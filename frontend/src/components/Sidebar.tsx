import React from "react";
import { FlowarrMark } from "./FlowarrMark";

export type PageDefinition = { name: string; icon: React.ComponentType<{ size?: number }> };

export function Sidebar({ pages, activePage, onNavigate }: { pages: PageDefinition[]; activePage: string; onNavigate: (page: string) => void }) {
  return (
    <aside className="fixed inset-y-0 left-0 hidden w-64 border-r border-line bg-surface px-4 py-5 lg:block">
      <div className="mb-8 flex items-center gap-3">
        <div className="grid h-10 w-10 place-items-center rounded-md bg-accent text-bg">
          <FlowarrMark size={22} />
        </div>
        <div>
          <h1 className="text-xl font-semibold text-ink">Flowarr</h1>
          <p className="text-xs text-muted">Media operations</p>
        </div>
      </div>
      <nav className="space-y-1">
        {pages.map((page) => {
          const active = activePage === page.name;
          return (
            <button
              key={page.name}
              onClick={() => onNavigate(page.name)}
              className={`relative flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm transition ${
                active ? "bg-accentMuted text-accent" : "text-muted hover:bg-surfaceMuted hover:text-ink"
              }`}
            >
              {active && <span className="absolute left-0 top-1/2 h-5 w-0.5 -translate-y-1/2 rounded-full bg-accent" />}
              <page.icon size={18} />
              {page.name}
            </button>
          );
        })}
      </nav>
    </aside>
  );
}

export function MobileNav({ pages, activePage, onNavigate }: { pages: PageDefinition[]; activePage: string; onNavigate: (page: string) => void }) {
  return (
    <div className="flex rounded-md border border-line bg-surface p-1 lg:hidden">
      {pages.map((page) => (
        <button
          key={page.name}
          title={page.name}
          onClick={() => onNavigate(page.name)}
          className={`grid h-10 w-10 place-items-center rounded ${activePage === page.name ? "bg-accentMuted text-accent" : "text-muted"}`}
        >
          <page.icon size={18} />
        </button>
      ))}
    </div>
  );
}
