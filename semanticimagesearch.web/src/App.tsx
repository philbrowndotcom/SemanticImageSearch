import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { getPeople, imageUrl, searchImages } from './api/search.ts'
import type { SearchResult } from './api/search.ts'
import './App.css'

const LIMIT_OPTIONS = [10, 25, 50]

function formatDate(value: string | null): string {
  if (!value) return 'Unknown date'
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? 'Unknown date'
    : date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

function App() {
  const [query, setQuery] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [availablePeople, setAvailablePeople] = useState<string[]>([])
  const [selectedPeople, setSelectedPeople] = useState<string[]>([])
  const [limit, setLimit] = useState(25)
  const [results, setResults] = useState<SearchResult[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [hasSearched, setHasSearched] = useState(false)
  const abortRef = useRef<AbortController | null>(null)

  const dateRangeInvalid = from !== '' && to !== '' && from > to
  const hasFilters = selectedPeople.length > 0 || from !== '' || to !== ''

  useEffect(() => {
    const controller = new AbortController()
    getPeople(controller.signal)
      .then(setAvailablePeople)
      .catch((err) => {
        if (err instanceof DOMException && err.name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Could not load people.')
      })
    return () => controller.abort()
  }, [])

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const trimmed = query.trim()
    if ((!trimmed && !hasFilters) || dateRangeInvalid) return

    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller

    setLoading(true)
    setError(null)
    try {
      const data = await searchImages(
        {
          query: trimmed || undefined,
          from: from || undefined,
          to: to || undefined,
          people: selectedPeople,
          limit,
        },
        controller.signal,
      )
      setResults(data)
      setHasSearched(true)
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') return
      setError(err instanceof Error ? err.message : 'Search failed.')
      setResults([])
    } finally {
      if (abortRef.current === controller) setLoading(false)
    }
  }

  function handleClear() {
    abortRef.current?.abort()
    setQuery('')
    setFrom('')
    setTo('')
    setSelectedPeople([])
    setResults([])
    setError(null)
    setHasSearched(false)
    setLoading(false)
  }

  return (
    <div className="app">
      <header className="app-header">
        <h1>Semantic Image Search</h1>
        <p>Describe what you're looking for and optionally limit by the date the photo was taken.</p>
      </header>

      <form className="search-form" onSubmit={handleSubmit}>
        <div className="field field-query">
          <label htmlFor="query">Search</label>
          <input
            id="query"
            type="search"
            placeholder="e.g. dog playing on the beach at sunset"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            autoFocus
          />
        </div>

        <div className="field people-field">
          <label htmlFor="people">People</label>
          <select
            id="people"
            multiple
            value={selectedPeople}
            onChange={(e) =>
              setSelectedPeople(Array.from(e.target.selectedOptions, (option) => option.value))
            }
            size={Math.min(Math.max(availablePeople.length, 2), 5)}
            disabled={availablePeople.length === 0}
          >
            {availablePeople.map((person) => (
              <option key={person} value={person}>
                {person}
              </option>
            ))}
          </select>
          {availablePeople.length === 0 && <small>No identified people available</small>}
        </div>

        <div className="field">
          <label htmlFor="from">Taken from</label>
          <input
            id="from"
            type="date"
            value={from}
            max={to || undefined}
            onChange={(e) => setFrom(e.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="to">Taken to</label>
          <input
            id="to"
            type="date"
            value={to}
            min={from || undefined}
            onChange={(e) => setTo(e.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="limit">Results</label>
          <select id="limit" value={limit} onChange={(e) => setLimit(Number(e.target.value))}>
            {LIMIT_OPTIONS.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </div>

        <div className="actions">
          <button type="submit" disabled={loading || (!query.trim() && !hasFilters) || dateRangeInvalid}>
            {loading ? 'Searching...' : 'Search'}
          </button>
          <button type="button" className="secondary" onClick={handleClear}>
            Clear
          </button>
        </div>
      </form>

      {dateRangeInvalid && <p className="message error">"Taken from" must be on or before "Taken to".</p>}
      {error && <p className="message error">{error}</p>}
      {!loading && !error && hasSearched && results.length === 0 && (
        <p className="message">No images matched your search.</p>
      )}

      <ul className="results">
        {results.map((r) => (
          <li key={r.id} className="card">
            <a
              href={imageUrl(r.id)}
              target="_blank"
              rel="noreferrer"
              className="thumb"
              title={r.description ?? undefined}
            >
              <img src={imageUrl(r.id)} alt={r.description ?? 'Image'} loading="lazy" />
            </a>
            <div className="card-body">
              <div className="card-meta">
                <span>{formatDate(r.dateTaken)}</span>
                <span className="score" title="Similarity score">
                  {(r.score * 100).toFixed(1)}%
                </span>
              </div>
              {r.peopleNames?.length > 0 && (
                <p className="people-names"><strong>People:</strong> {r.peopleNames.join(', ')}</p>
              )}
              {r.description && <p className="description">{r.description}</p>}
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}

export default App
