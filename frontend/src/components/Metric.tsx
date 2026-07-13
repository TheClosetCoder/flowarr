import React from "react";
import { Card } from "./Card";

export function Metric({ title, value, sub, icon: Icon }: { title: string; value: string; sub: string; icon?: React.ComponentType<{ size?: number; className?: string }> }) {
  return (
    <Card className="flex items-start justify-between gap-3">
      <div>
        <p className="text-sm text-muted">{title}</p>
        <p className="mt-2 text-2xl font-semibold text-ink">{value}</p>
        <p className="mt-1 text-xs text-muted">{sub}</p>
      </div>
      {Icon && (
        <div className="grid h-10 w-10 flex-none place-items-center rounded-md bg-accentMuted text-accent">
          <Icon size={20} />
        </div>
      )}
    </Card>
  );
}
