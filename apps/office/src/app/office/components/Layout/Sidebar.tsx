/**
 * Menu lateral esquerdo do Office. Ainda não possui itens de navegação
 * definidos — a lista de itens do menu é uma decisão de produto que será
 * feita em uma próxima etapa. Por enquanto, reserva apenas o espaço
 * visual à esquerda do miolo, na mesma paleta escura do header.
 */
export function Sidebar() {
  return (
    <aside
      aria-label="Menu lateral"
      className="hidden w-56 shrink-0 border-r border-slate-800 bg-slate-950 text-slate-100 sm:block"
    >
      <nav aria-label="Navegação principal" className="sticky top-16 space-y-1 p-4">
        <p className="px-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
          Menu
        </p>
        <p className="rounded-md px-2 py-2 text-sm text-slate-400">
          Itens do menu em definição.
        </p>
      </nav>
    </aside>
  )
}
