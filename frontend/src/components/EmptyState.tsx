import React from "react";

export function EmptyState({ message, icon: Icon }: { message: string; icon?: React.ComponentType<{ size?: number; className?: string }> }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-md border border-dashed border-line py-8 text-center">
      {Icon && <Icon size={22} className="text-muted" />}
      <p className="text-sm text-muted">{message}</p>
    </div>
  );
}
