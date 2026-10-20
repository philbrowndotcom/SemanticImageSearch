export interface SearchResult {
  id: string
  filePath: string | null
  description: string | null
  dateTaken: string | null
  score: number
}

export interface SearchParams {
  query: string
  from?: string
  to?: string
  limit: number
}

export async function searchImages(
  { query, from, to, limit }: SearchParams,
  signal?: AbortSignal,
): Promise<SearchResult[]> {
  const params = new URLSearchParams({ query, limit: String(limit) })
  if (from) params.set('from', from)
  if (to) params.set('to', to)

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
