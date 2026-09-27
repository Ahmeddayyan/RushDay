import { BookOpen } from 'lucide-react'

import { PageHeader } from '@/components/layout/PageHeader'
import {
  EmptyState,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/components/ui'
import { usePageTitle } from '@/lib/usePageTitle'

export function ModulesPage() {
  usePageTitle('Modules')

  return (
    <>
      <PageHeader
        title="Modules"
        description="The catalogue, with places remaining on each module."
      />
      <Table>
        <TableHead>
          <TableRow className="hover:bg-transparent">
            <TableHeaderCell>Code</TableHeaderCell>
            <TableHeaderCell>Title</TableHeaderCell>
            <TableHeaderCell className="text-right">Credits</TableHeaderCell>
            <TableHeaderCell>Semester</TableHeaderCell>
            <TableHeaderCell className="text-right">Places</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          <TableRow className="hover:bg-transparent">
            <TableCell colSpan={5} className="p-0">
              <EmptyState
                compact
                icon={BookOpen}
                title="The catalogue is on its way"
                description="Modules will load from the API once this page is wired up."
              />
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </>
  )
}
