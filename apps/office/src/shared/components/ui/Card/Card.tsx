import type { HTMLAttributes } from 'react'
import { cn } from '../../../lib/cn'

type CardProps = HTMLAttributes<HTMLDivElement>

/** Contêiner base do compound component `Card` (ver Card.Header/Body/Footer). */
function CardRoot({ className, children, ...props }: CardProps) {
  return (
    <div
      className={cn('rounded-lg border border-slate-200 bg-white shadow-sm', className)}
      {...props}
    >
      {children}
    </div>
  )
}

function CardHeader({ className, children, ...props }: CardProps) {
  return (
    <div
      className={cn('flex items-center justify-between gap-3 border-b border-slate-100 px-4 py-3', className)}
      {...props}
    >
      {children}
    </div>
  )
}

function CardBody({ className, children, ...props }: CardProps) {
  return (
    <div className={cn('p-4', className)} {...props}>
      {children}
    </div>
  )
}

function CardFooter({ className, children, ...props }: CardProps) {
  return (
    <div className={cn('border-t border-slate-100 px-4 py-3', className)} {...props}>
      {children}
    </div>
  )
}

/**
 * Compound component reutilizável para os cards do design system.
 *
 * Uso: `<Card><Card.Header>...</Card.Header><Card.Body>...</Card.Body></Card>`.
 */
export const Card = Object.assign(CardRoot, {
  Header: CardHeader,
  Body: CardBody,
  Footer: CardFooter,
})
