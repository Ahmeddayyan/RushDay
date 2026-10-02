import { Card, CardHeader, CardTitle } from '@/components/ui'

const TERMS: { term: string; definition: string }[] = [
  {
    term: 'Requests per second',
    definition: 'How many pages or actions the server answered each second.',
  },
  { term: 'p95', definition: '19 of 20 requests were faster than this.' },
  { term: 'Oversold', definition: 'More students given a place than there are places.' },
  {
    term: 'Turned away',
    definition: 'Asked to try again straight away instead of being left waiting.',
  },
]

/** The plain-language glossary card of `/story` (05-frontend.md section 10). */
export function Glossary() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Terms used on this page</CardTitle>
      </CardHeader>
      <dl className="grid gap-4 sm:grid-cols-2">
        {TERMS.map(({ term, definition }) => (
          <div key={term}>
            <dt className="font-medium text-text">{term}</dt>
            <dd className="text-sm text-muted">{definition}</dd>
          </div>
        ))}
      </dl>
    </Card>
  )
}
