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
    let message = `Search failed (${response.status})`
    try {
      const body = await response.text()
      if (body) {
        try {
          const parsed = JSON.parse(body)
          message = parsed.error ?? parsed.title ?? (typeof parsed === 'string' ? parsed : message)
        } catch {
          message = body
        }
      }
    } catch {
      // ignore body read errors
    }
    throw new Error(message)
  }

  return (await response.json()) as SearchResult[]
}

export function imageUrl(id: string): string {
  return `/api/images/${encodeURIComponent(id)}`
}

export async function getPeople(signal?: AbortSignal): Promise<string[]> {
  const response = await fetch('/api/people', { signal })
  if (!response.ok) {
    throw new Error(`Could not load people (${response.status})`)
  }

  return (await response.json()) as string[]
}
