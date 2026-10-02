import { useId, type ReactNode } from 'react'

import {
  Card,
  CardHeader,
  CardTitle,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui'
import { EmptyState } from '@/components/ui'

export interface ChartCardProps {
  title: string
  /** One plain sentence, e.g. "Before, 154 students got 30 places. After, exactly 30 did." */
  whatThisMeans: ReactNode
  /**
   * Named as plain text, not a link: the ADRs that record these decisions are stage S11's to write
   * (docs/adr/0007-0012) and do not exist yet while this stage runs, so this stage never guesses a
   * file path for them. ADR 5 ("v0 was deliberately naive") is already named in 00-overview.md and
   * safe to cite directly.
   */
  decisionRecord?: ReactNode
  /** Repository-relative paths to the raw k6 summaries behind this chart (informational, not a link:
   * these files live outside `public/` and are not served to the browser). */
  rawSources?: string[]
  /** Read by assistive technology in place of the figure: the same fact as `whatThisMeans`, self-contained. */
  summary: string
  /** Content shown above the Chart/Table tabs in both views, e.g. a hero figure. */
  lead?: ReactNode
  chart: ReactNode
  table: ReactNode
  /** No run exists for this scenario yet (e.g. login-storm before any v1 run is committed). */
  empty?: ReactNode
  className?: string
}

/**
 * Wraps one load-test chart (05-frontend.md section 11): a title, a Chart/Table toggle (`ChartTable`
 * renders the identical numbers as the chart), a visually hidden summary sentence for the figure,
 * "What this means", "Decision record" and "Raw test output". Used by both `/story` and the "load
 * story" section of `/admin/ops`; reaching this module only through those two lazy routes is what
 * keeps Recharts out of the entry bundle (05-frontend.md section 3).
 */
export function ChartCard({
  title,
  whatThisMeans,
  decisionRecord,
  rawSources,
  summary,
  lead,
  chart,
  table,
  empty,
  className,
}: ChartCardProps) {
  const headingId = useId()

  return (
    <Card className={className}>
      <CardHeader>
        <CardTitle id={headingId}>{title}</CardTitle>
      </CardHeader>

      {empty ? (
        <EmptyState compact title="Not yet measured" description={empty} />
      ) : (
        <figure aria-labelledby={headingId} className="m-0">
          <figcaption className="sr-only">{summary}</figcaption>
          {lead}
          <Tabs defaultValue="chart">
            <TabsList aria-label={`${title}: chart or table`}>
              <TabsTrigger value="chart">Chart</TabsTrigger>
              <TabsTrigger value="table">Table</TabsTrigger>
            </TabsList>
            <TabsContent value="chart">{chart}</TabsContent>
            <TabsContent value="table">{table}</TabsContent>
          </Tabs>
        </figure>
      )}

      <div className="mt-4 space-y-1.5 border-t border-border pt-4 text-sm">
        <p className="text-text">
          <span className="font-medium">What this means: </span>
          {whatThisMeans}
        </p>
        {decisionRecord && (
          <p className="text-muted">
            <span className="font-medium text-text">Decision record: </span>
            {decisionRecord}
          </p>
        )}
        {rawSources && rawSources.length > 0 && (
          <p className="text-muted">
            <span className="font-medium text-text">Raw test output: </span>
            {rawSources.map((source, index) => (
              <span key={source}>
                <code className="font-mono text-xs">{source}</code>
                {index < rawSources.length - 1 ? ', ' : ''}
              </span>
            ))}
          </p>
        )}
      </div>
    </Card>
  )
}
