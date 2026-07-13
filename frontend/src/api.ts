export const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";

export async function fetchJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, { credentials: "include" });
  if (!response.ok) throw new Error(`Request failed: ${path}`);
  return response.json();
}

export async function putJson(path: string, body: unknown) {
  const response = await fetch(`${apiBase}${path}`, {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await getErrorMessage(response, path));
}

export async function postJson<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    method: "POST",
    credentials: "include",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await getErrorMessage(response, path));
  return response.json();
}

async function getErrorMessage(response: Response, path: string) {
  const body = await response.text();
  if (!body) return `Request failed: ${path} (${response.status})`;

  try {
    const parsed = JSON.parse(body) as { message?: string; title?: string; errors?: Record<string, string[]> };
    if (parsed.message) return parsed.message;
    if (parsed.errors) return Object.entries(parsed.errors).map(([field, values]) => `${field}: ${values.join(", ")}`).join(" ");
    if (parsed.title) return parsed.title;
  } catch {
    return body;
  }

  return `Request failed: ${path} (${response.status})`;
}

export function createClientId() {
  return globalThis.crypto?.randomUUID?.() ?? `local-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}
