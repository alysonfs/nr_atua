import type { KeyboardEvent, ReactNode } from 'react'
import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { cn } from '../../../lib/cn'

interface DialogProps {
  /** Controla a montagem/visibilidade do diálogo. */
  open: boolean
  /** Chamado ao pressionar Esc, clicar fora ou no botão de fechar padrão. */
  onClose: () => void
  /** `aria-label` do diálogo (acessibilidade). */
  ariaLabel: string
  /** Conteúdo do diálogo. */
  children: ReactNode
  /** Classe adicional para o painel do diálogo (largura/altura). */
  className?: string
}

/**
 * Diálogo modal acessível e reutilizável, renderizado via portal em
 * `document.body`. Usado como base para modais do Office (ex.: visualização
 * ampliada de anexos no detalhe da triagem).
 *
 * Comportamento de acessibilidade:
 * - `role="dialog"` + `aria-modal="true"` + `aria-label`.
 * - Fecha com Esc, clique no overlay (fora do painel) ou botão de fechar.
 * - Foco movido para o painel ao abrir e devolvido ao elemento anterior ao
 *   fechar.
 * - Scroll do body bloqueado enquanto aberto.
 */
export function Dialog({ open, onClose, ariaLabel, children, className }: DialogProps) {
  const panelRef = useRef<HTMLDivElement>(null)
  const previouslyFocusedRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (!open) return

    previouslyFocusedRef.current = document.activeElement as HTMLElement | null
    panelRef.current?.focus()

    const originalOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    return () => {
      document.body.style.overflow = originalOverflow
      previouslyFocusedRef.current?.focus?.()
    }
  }, [open])

  if (!open) return null

  function handleOverlayClick(event: React.MouseEvent<HTMLDivElement>) {
    if (event.target === event.currentTarget) {
      onClose()
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.stopPropagation()
      onClose()
    }
  }

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4"
      onMouseDown={handleOverlayClick}
      onKeyDown={handleKeyDown}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel}
        tabIndex={-1}
        className={cn(
          'relative max-h-[90vh] w-full max-w-4xl overflow-hidden rounded-lg bg-white shadow-xl outline-none',
          className,
        )}
      >
        {children}
      </div>
    </div>,
    document.body,
  )
}

export default Dialog
