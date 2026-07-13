export function Table({ headers, rows }: { headers: string[]; rows: string[][] }) {
  return (
    <div className="overflow-hidden rounded-lg border border-line bg-surface">
      <div className="overflow-x-auto">
        <table className="min-w-full text-left text-sm">
          <thead className="bg-surfaceMuted text-muted">
            <tr>{headers.map((header) => <th key={header} className="px-4 py-3 font-medium">{header}</th>)}</tr>
          </thead>
          <tbody>
            {rows.length ? rows.map((row, index) => (
              <tr key={`${row[0]}-${index}`} className="border-t border-line hover:bg-surfaceMuted/50">
                {row.map((cell, cellIndex) => <td key={`${cell}-${cellIndex}`} className="max-w-sm break-words px-4 py-3 text-ink">{cell}</td>)}
              </tr>
            )) : (
              <tr>
                <td colSpan={headers.length} className="px-4 py-6 text-center text-muted">No data yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
