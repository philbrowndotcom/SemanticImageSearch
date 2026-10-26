export interface SearchResult {
  id: string
  filePath: string | null
  description: string | null
  peopleNames: string[]
  dateTaken: string | null
  score: number
}

export interface SearchParams {
  query?: string
  from?: string
  to?: string
  people?: string[]
  limit: number
}

export async function searchImages(
  { query, from, to, people, limit }: SearchParams,
  signal?: AbortSignal,
): Promise<SearchResult[]> {
  const params = new URLSearchParams({ limit: String(limit) })
  if (query?.trim()) params.set('query', query.trim())
  if (from) params.set('from', from)
  if (to) params.set('to', to)
  people?.forEach((person) => params.append('people', person))

  const response = await fetch(`/api/search?${params.toString()}`, { signal })
  if (!response.ok) {
    throw new Error(await readErrorMessage(response, `Search failed (${response.status})`))
  }

  return (await response.json()) as SearchResult[]
}

// Uses the API's { error } message (e.g. "Cannot reach Qdrant...") when there is one.
async function readErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = await response.text()
    if (!body) return fallback
    try {
      const parsed = JSON.parse(body)
      return parsed.error ?? parsed.title ?? (typeof parsed === 'string' ? parsed : fallback)
    } catch {
      return body
    }
  } catch {
    return fallback
  }
}

export function imageUrl(id: string): string {
  return `/api/images/${encodeURIComponent(id)}`
}

export async function getPeople(signal?: AbortSignal): Promise<string[]> {
  const response = await fetch('/api/people', { signal })
  if (!response.ok) {
    throw new Error(await readErrorMessage(response, `Could not load people (${response.status})`))
  }

  return (await response.json()) as string[]
}
