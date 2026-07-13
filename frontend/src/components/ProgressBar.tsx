export function ProgressBar({ value }: { value: number }) {
  const percent = Math.round(Math.min(1, Math.max(0, value)) * 100);

  return (
    <div className="h-1.5 overflow-hidden rounded-full bg-surfaceMuted">
      <div
        className="h-full rounded-full bg-gradient-to-r from-accent/70 to-accent transition-all"
        style={{ width: `${percent}%` }}
      />
    </div>
  );
}
