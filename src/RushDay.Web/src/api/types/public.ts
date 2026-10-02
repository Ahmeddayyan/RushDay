import type { PublicationBrief, Role, Semester, WindowInfo } from './common'

/** Shapes of the anonymous routes of 02-api.md section 8.1 (`GET /api`, health, public status). */

export interface ApiIndexLinks {
  health: string
  ready: string
  status: string
  login: string
  github: string
  /** Present only in Development. */
  openapi?: string
}

/** `GET /api` */
export interface ApiIndex {
  name: 'RushDay'
  /** The owner's story sentence pair, verbatim. */
  story: string
  commit: string
  environment: string
  links: ApiIndexLinks
}

/** `GET /api/health/live` */
export interface LiveResponse {
  status: 'Healthy'
}

/** `GET /api/health/ready` */
export interface ReadyResponse {
  status: 'Healthy' | 'Unhealthy'
  checks: { name: string; status: string; durationMs: number }[]
}

export interface SupportInfo {
  email: string | null
  url: string | null
}

export interface InstitutionInfo {
  name: string
  shortName: string
  /** IANA id, for example "Europe/London"; only the browser formats with it (D26). */
  timeZone: string
  privacyNoticeUrl: string | null
  resultsFootnote: string
  /** Null when neither a support email nor a URL is set. */
  support: SupportInfo | null
}

export interface DemoAccountInfo {
  role: Role
  username: string
  password: string
  /** Static text (01-domain-and-data.md section 7): never contains a formatted date. */
  hint: string
}

export interface DemoInfo {
  accounts: DemoAccountInfo[]
}

/** `GET /api/public/status` */
export interface PublicStatus {
  serverTime: string
  institution: InstitutionInfo
  academicYear: string
  currentSemester: Semester
  /** Earliest publish_at > now. */
  nextPublication: PublicationBrief | null
  /** Latest publish_at <= now. */
  latestPublication: PublicationBrief | null
  enrolmentWindows: WindowInfo[]
  /** Null unless Demo:Enabled. */
  demo: DemoInfo | null
}
