import type { Api } from './fixtures.ts'

/**
 * CS3099, the demo's hot module (a Spring module with 30 places, led by L00001), is shared by the
 * enrolment journey and the marks journey. Both start from `resetHotModule`, so neither depends on
 * the order the specs run in or on what a failed earlier attempt left behind.
 */
export const HOT_MODULE = 'CS3099'
export const HOT_MODULE_CAPACITY = 30
const REASON = 'Reset by the end-to-end suite before a journey.'

interface MarksStatus {
  status: 'noStudents' | 'draft' | 'submitted' | 'scheduled' | 'published'
}

interface AdminResults {
  academicYear: string
  modules: { code: string; marks: MarksStatus }[]
  publications: { id: string; state: 'scheduled' | 'live' }[]
}

interface Roster {
  items: { studentNumber: string; status: 'active' | 'withdrawn' }[]
}

interface ModuleDetail {
  code: string
  title: string
  description: string | null
  credits: number
  capacity: number
  enrolledCount: number
  placesRemaining: number
  semester: 'autumn' | 'spring'
  isActive: boolean
}

export async function hotModule(admin: Api): Promise<ModuleDetail> {
  return admin.get<ModuleDetail>(`/api/modules/${HOT_MODULE}`)
}

export async function setHotModuleCapacity(admin: Api, capacity: number): Promise<void> {
  const module = await hotModule(admin)
  if (module.capacity === capacity && module.isActive) return
  await admin.put(`/api/admin/modules/${HOT_MODULE}`, {
    title: module.title,
    description: module.description,
    credits: module.credits,
    capacity,
    semester: module.semester,
    isActive: true,
  })
}

/**
 * Brings CS3099 back to "marks in draft, nobody enrolled this year, 30 places": a live Spring
 * publication is unpublished and a scheduled one cancelled (on the e2e database only CS3099 is ever
 * published for Spring of the current year), a submitted module returns to draft, and every active
 * enrolment of the year is withdrawn by the administrator.
 */
export async function resetHotModule(admin: Api): Promise<void> {
  const { academicYear } = await admin.get<{ academicYear: string }>('/api/admin/settings')
  const resultsPath = `/api/admin/results?semester=spring&academicYear=${encodeURIComponent(academicYear)}`
  const status = async () =>
    (await admin.get<AdminResults>(resultsPath)).modules.find((m) => m.code === HOT_MODULE)?.marks
      .status

  let marks = await status()
  if (marks === 'published' || marks === 'scheduled') {
    const { publications } = await admin.get<AdminResults>(resultsPath)
    for (const publication of publications) {
      if (publication.state === 'live') {
        await admin.post(`/api/admin/results/publications/${publication.id}/unpublish`, {
          reason: REASON,
        })
      } else {
        await admin.delete(`/api/admin/results/publications/${publication.id}`)
      }
    }
    marks = await status()
  }
  if (marks === 'submitted') {
    await admin.post(`/api/admin/results/modules/${HOT_MODULE}/return-to-draft`, {
      reason: REASON,
      academicYear,
    })
  }

  const roster = await admin.get<Roster>(`/api/admin/modules/${HOT_MODULE}/roster?pageSize=200`)
  for (const row of roster.items) {
    if (row.status !== 'active') continue
    await admin.post(`/api/admin/students/${row.studentNumber}/enrolments/${HOT_MODULE}/withdraw`, {
      reason: REASON,
    })
  }

  await setHotModuleCapacity(admin, HOT_MODULE_CAPACITY)
}
