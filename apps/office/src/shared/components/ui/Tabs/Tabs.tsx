import { useState, type ReactNode } from 'react'
import { cn } from '../../../lib/cn'

export interface TabItem {
  id: string
  label: string
  content: ReactNode
}

interface TabsProps {
  items: TabItem[]
  /** Id da aba ativa por padrão; cai para `items[0].id` quando omitido. */
  defaultTabId?: string
}

/**
 * Abas simples, controladas internamente (sem sincronismo com a URL),
 * seguindo o padrão de acessibilidade WAI-ARIA (`tablist`/`tab`/`tabpanel`).
 */
export function Tabs({ items, defaultTabId }: TabsProps) {
  const [activeId, setActiveId] = useState(defaultTabId ?? items[0]?.id)
  const activeItem = items.find((item) => item.id === activeId) ?? items[0]

  return (
    <div>
      <div role="tablist" className="flex gap-2 border-b border-slate-200">
        {items.map((item) => {
          const isActive = item.id === activeItem?.id
          return (
            <button
              key={item.id}
              type="button"
              role="tab"
              aria-selected={isActive}
              aria-controls={`tabpanel-${item.id}`}
              id={`tab-${item.id}`}
              onClick={() => setActiveId(item.id)}
              className={cn(
                'border-b-2 px-3 py-2 text-sm font-medium transition-colors',
                isActive
                  ? 'border-slate-900 text-slate-950'
                  : 'border-transparent text-slate-500 hover:text-slate-700',
              )}
            >
              {item.label}
            </button>
          )
        })}
      </div>

      {activeItem && (
        <div role="tabpanel" id={`tabpanel-${activeItem.id}`} aria-labelledby={`tab-${activeItem.id}`} className="pt-4">
          {activeItem.content}
        </div>
      )}
    </div>
  )
}
