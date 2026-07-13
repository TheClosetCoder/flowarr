import React from "react";
import { Info } from "lucide-react";

export function SectionHeader({
  title,
  description,
  action,
  size = "md"
}: {
  title: string;
  description?: string;
  action?: React.ReactNode;
  size?: "sm" | "md";
}) {
  const TitleTag = size === "sm" ? "h4" : "h3";
  const titleClass = size === "sm" ? "text-sm font-medium text-ink" : "font-medium text-ink";

  return (
    <div className="flex items-center justify-between gap-3">
      <div className="flex items-center gap-2">
        <TitleTag className={titleClass}>{title}</TitleTag>
        {description && (
          <span className="group relative inline-flex">
            <Info size={15} className="text-muted" />
            <span className="pointer-events-none absolute left-1/2 top-6 z-20 hidden w-72 -translate-x-1/2 rounded-md border border-line bg-surfaceMuted px-3 py-2 text-xs font-normal text-ink shadow-xl group-hover:block">
              {description}
            </span>
          </span>
        )}
      </div>
      {action}
    </div>
  );
}
