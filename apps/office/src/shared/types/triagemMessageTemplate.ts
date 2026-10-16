export type TriagemMessageTemplateType =
  | 'InitialContact'
  | 'Reminder24Hours'
  | 'Reminder36Hours'

export type TriagemMessageTemplateStatus = 'Draft' | 'Published'

export interface TriagemMessageTemplate {
  id: string
  type: TriagemMessageTemplateType
  version: number
  content: string
  status: TriagemMessageTemplateStatus
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string
  publishedAtUtc: string | null
  validationErrors: string[]
}

export interface TriagemMessageTemplatesResponse {
  templates: TriagemMessageTemplate[]
  validationErrors: string[]
  preview: string | null
}

export type TriagemMessageTemplateErrorCode =
  | 'integration_not_found'
  | 'invalid_template_content'
  | 'template_draft_not_found'
  | 'forbidden'
  | 'validation_failed'
  | 'template_concurrency_conflict'
  | 'unknown_error'
