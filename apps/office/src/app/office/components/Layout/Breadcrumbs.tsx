import { Fragment, useMemo } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import useBreadcrumbs, { type BreadcrumbsRoute } from 'use-react-router-breadcrumbs'

/**
 * Trilha de navegação (breadcrumbs) das rotas protegidas do Office, exibida
 * logo abaixo do DashboardHeader. Substitui links avulsos de "voltar" nas
 * páginas internas (ex.: WorkOrderDetailPage).
 */
export function Breadcrumbs() {
  const { t } = useTranslation()

  const routes = useMemo<BreadcrumbsRoute[]>(
    () => [
      { path: '/', breadcrumb: t('layout.breadcrumbs.home') },
      { path: '/settings', breadcrumb: t('layout.breadcrumbs.settings') },
      { path: '/work-orders/:workOrderId', breadcrumb: t('layout.breadcrumbs.workOrderDetail') },
    ],
    [t],
  )

  const breadcrumbs = useBreadcrumbs(routes, { disableDefaults: true })

  if (breadcrumbs.length <= 1) return null

  return (
    <nav aria-label={t('layout.breadcrumbs.ariaLabel')} className="border-b border-slate-200 bg-white px-4 py-2.5 sm:px-6 lg:px-8">
      <ol className="mx-auto flex max-w-5xl items-center gap-1.5 text-sm text-slate-500">
        {breadcrumbs.map(({ match, breadcrumb }, index) => {
          const isLast = index === breadcrumbs.length - 1
          return (
            <Fragment key={match.pathname}>
              {index > 0 && (
                <span aria-hidden="true" className="text-slate-300">
                  /
                </span>
              )}
              <li>
                {isLast ? (
                  <span className="font-medium text-slate-800" aria-current="page">
                    {breadcrumb}
                  </span>
                ) : (
                  <Link to={match.pathname === '/' ? '/home' : match.pathname} className="text-sky-700 hover:underline">
                    {breadcrumb}
                  </Link>
                )}
              </li>
            </Fragment>
          )
        })}
      </ol>
    </nav>
  )
}
