import type { HTMLAttributes } from 'react'
import { cn } from '../../../lib/cn'
import type { StatusTone } from '../../../../app/dashboard/lib/providerStatus'

interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  /** Tonalidade centralizada (tokens `--color-status-*`), nunca hex direto. */
  tone: StatusTone
}

const TONE_CLASSES: Record<StatusTone, string> = {
  success: 'bg-status-success/10 text-status-success ring-1 ring-inset ring-status-success/20',
  danger: 'bg-status-danger/10 text-status-danger ring-1 ring-inset ring-status-danger/20',
  info: 'bg-status-info/10 text-status-info ring-1 ring-inset ring-status-info/20',
  warning: 'bg-status-warning/10 text-status-warning ring-1 ring-inset ring-status-warning/20',
  neutral: 'bg-status-neutral/10 text-status-neutral ring-1 ring-inset ring-status-neutral/20',
}

/** Pill de status reutilizável: recebe uma tonalidade (nunca hex) mapeada para os tokens centralizados. */
export function Badge({ tone, className, children, ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        TONE_CLASSES[tone],
        className,
      )}
      {...props}
    >
      {children}
    </span>
  )
}
