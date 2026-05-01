const apiBase = "/api/v1";

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public body?: string
  ) {
    super(message);
  }
}

async function parseJson<T>(r: Response): Promise<T> {
  const text = await r.text();
  if (!text) return {} as T;
  return JSON.parse(text) as T;
}

export async function apiGet<T>(path: string): Promise<T> {
  const r = await fetch(`${apiBase}${path}`);
  if (!r.ok) {
    throw new ApiError(`HTTP ${r.status}`, r.status, await r.text());
  }
  return parseJson<T>(r);
}

export async function apiSend<T>(
  path: string,
  method: string,
  body?: unknown
): Promise<T> {
  const r = await fetch(`${apiBase}${path}`, {
    method,
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const text = await r.text();
  if (!r.ok) {
    throw new ApiError(text || r.statusText, r.status, text);
  }
  if (!text) return {} as T;
  return JSON.parse(text) as T;
}
