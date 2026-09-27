import { BookOpen, CalendarDays, GraduationCap } from 'lucide-react'

import { PageHeader } from '@/components/layout/PageHeader'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  EmptyState,
} from '@/components/ui'
import { usePageTitle } from '@/lib/usePageTitle'

export function DashboardPage() {
  usePageTitle('Dashboard')

  return (
    <>
      <PageHeader
        title="Dashboard"
        description="Your results, timetable and enrolments in one place."
      />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>Results</CardTitle>
            <CardDescription>Published marks and your weighted average.</CardDescription>
          </CardHeader>
          <CardContent>
            <EmptyState
              compact
              icon={GraduationCap}
              title="No results to show yet"
              description="Marks appear here the moment they are published."
            />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Timetable</CardTitle>
            <CardDescription>This week, by day.</CardDescription>
          </CardHeader>
          <CardContent>
            <EmptyState
              compact
              icon={CalendarDays}
              title="No timetable yet"
              description="Sessions for your enrolled modules will be listed here."
            />
          </CardContent>
        </Card>
        <Card className="sm:col-span-2 xl:col-span-1">
          <CardHeader>
            <CardTitle>Enrolments</CardTitle>
            <CardDescription>
              Modules you are on, and places left on the ones you want.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <EmptyState
              compact
              icon={BookOpen}
              title="No enrolments yet"
              description="Browse the module catalogue to enrol."
            />
          </CardContent>
        </Card>
      </div>
    </>
  )
}
