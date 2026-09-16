import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import App from '../App'
import {
  addLocaleToUrl,
  OFFICE_SIGNIN_URL,
  OFFICE_SIGNUP_URL,
  SUPPORT_EMAIL,
} from '../constants'
import i18n, {
  EXPLICIT_LOCALE_STORAGE_KEY,
  LOCALE_STORAGE_KEY,
  normalizeLocale,
  persistLocale,
  resolveInitialLocale,
} from '../i18n'

describe('Landing page', () => {
  beforeEach(async () => {
    localStorage.clear()
    persistLocale('pt-BR', false)
    await i18n.changeLanguage('pt-BR')
  })

  it('renders the hero headline, subheadline and primary action buttons', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', {
        level: 1,
        name: 'Sua operação técnica, unificada em uma plataforma para vários provedores.',
      }),
    ).toBeInTheDocument()

    expect(
      screen.getByText(/começando pelo conector iService e preparado para evoluir/),
    ).toBeInTheDocument()

    const heroCtas = screen.getAllByRole('link', { name: 'Começar grátis' })
    expect(heroCtas[0]).toHaveAttribute('href', OFFICE_SIGNUP_URL)

    const heroSignIn = screen.getAllByRole('link', { name: 'Entrar' })
    expect(heroSignIn[0]).toHaveAttribute('href', OFFICE_SIGNIN_URL)

    expect(screen.getByText('Vários provedores')).toBeInTheDocument()
  })

  it('renders section 1 - "Uma plataforma para toda a operação"', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', { name: 'Uma plataforma para toda a operação' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Base para vários provedores')).toBeInTheDocument()
    expect(screen.getByText(/fontes dispersas em dados organizados/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Dados organizados' })).toBeInTheDocument()
  })

  it('renders section 2 - conectores e provedores', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', {
        name: 'Comece pelos provedores que sua operação já usa',
      }),
    ).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'iService' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Novos provedores' })).toBeInTheDocument()
  })

  it('renders section 3 - argumento de confiança sobre evolução por etapas', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', { name: 'Primeiro leitura confiável. Depois automação com controle.' }),
    ).toBeInTheDocument()
    expect(
      screen.getByText(/Nesta fase, o ATUA coleta e organiza informações/),
    ).toBeInTheDocument()
    expect(screen.getByText('Fase de leitura')).toBeInTheDocument()
    expect(screen.getByText('Automação gradual')).toBeInTheDocument()
  })

  it('renders section 4 - "Comece em três passos" com os três passos numerados', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Comece em três passos' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Crie sua conta' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Conecte suas fontes' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Ative o Agente Coletor' })).toBeInTheDocument()
  })

  it('renders section 5 - chamada final com os links corretos', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', { name: 'Pronto para enxergar sua operação com mais clareza?' }),
    ).toBeInTheDocument()

    const createAccount = screen.getByRole('link', { name: 'Criar conta grátis' })
    expect(createAccount).toHaveAttribute('href', OFFICE_SIGNUP_URL)

    const signIn = screen.getByRole('link', { name: 'Já tenho conta — Entrar' })
    expect(signIn).toHaveAttribute('href', OFFICE_SIGNIN_URL)
  })

  it('renders the footer with tagline, links, dynamic year and early-stage notice', () => {
    render(<App />)

    const footer = screen.getByRole('contentinfo')
    const withinFooter = within(footer)

    expect(
      withinFooter.getByText('ATUA — Plataforma operacional para empresas de serviços técnicos.'),
    ).toBeInTheDocument()

    expect(withinFooter.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', OFFICE_SIGNIN_URL)
    expect(withinFooter.getByRole('link', { name: 'Criar conta' })).toHaveAttribute(
      'href',
      OFFICE_SIGNUP_URL,
    )
    expect(withinFooter.getByRole('link', { name: SUPPORT_EMAIL })).toHaveAttribute(
      'href',
      `mailto:${SUPPORT_EMAIL}`,
    )

    const currentYear = new Date().getFullYear()
    expect(
      withinFooter.getByText(
        `© ${currentYear} Assistência Técnica Unificada Ltda. Todos os direitos reservados.`,
      ),
    ).toBeInTheDocument()

    expect(
      withinFooter.getByText(
        /O ATUA está em fase inicial\. Algumas funcionalidades podem estar em desenvolvimento ou sujeitas a alteração\./,
      ),
    ).toBeInTheDocument()
  })

  it('does not position iService as the center of the landing narrative', () => {
    render(<App />)

    expect(screen.queryByText(/depende do iService/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Conecte ao iService/)).not.toBeInTheDocument()
    expect(screen.queryByText(/ao iService em um único ambiente/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Nunca interfere/)).not.toBeInTheDocument()
    expect(screen.queryByText(/intervalos regulares/)).not.toBeInTheDocument()
    expect(screen.queryByText(/tempo real/)).not.toBeInTheDocument()
  })

  it('changes every section to English without reloading', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(
      screen.getByRole('button', {
        name: 'Mudar idioma para English (United States)',
      }),
    )

    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: 'Your technical operations, unified in a platform for multiple providers.',
      }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { name: 'One platform for your entire operation' }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('heading', {
        name: 'Start with the providers your operation already uses',
      }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('heading', {
        name: 'Reliable data first. Then automation with control.',
      }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { name: 'Get started in three steps' }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { name: 'Ready to see your operation more clearly?' }),
    ).toBeInTheDocument()
    expect(
      within(screen.getByRole('contentinfo')).getByText(
        'ATUA — Operations platform for technical service companies.',
      ),
    ).toBeInTheDocument()
  })

  it('persists an explicit selection, updates the document language and hands it off to Office', async () => {
    const user = userEvent.setup()
    render(<App />)

    const selector = screen.getByRole('group', { name: 'Selecionar idioma' })
    expect(
      within(selector).getByRole('button', {
        name: 'Mudar idioma para Português (Brasil)',
      }),
    ).toHaveAttribute('aria-pressed', 'true')

    await user.click(
      within(selector).getByRole('button', {
        name: 'Mudar idioma para Español (Argentina)',
      }),
    )

    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: 'Tu operación técnica, unificada en una plataforma para múltiples proveedores.',
      }),
    ).toBeInTheDocument()
    expect(document.documentElement).toHaveAttribute('lang', 'es-AR')
    expect(document.title).toBe(
      'ATUA — Plataforma operativa para empresas de servicios técnicos',
    )
    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('es-AR')
    expect(localStorage.getItem(EXPLICIT_LOCALE_STORAGE_KEY)).toBe('true')

    const signUpUrl = addLocaleToUrl(OFFICE_SIGNUP_URL, 'es-AR')
    const signInUrl = addLocaleToUrl(OFFICE_SIGNIN_URL, 'es-AR')

    expect(screen.getByRole('link', { name: 'Comenzar gratis' })).toHaveAttribute(
      'href',
      signUpUrl,
    )
    expect(
      screen.getByRole('link', { name: 'Crear cuenta gratis' }),
    ).toHaveAttribute('href', signUpUrl)
    expect(
      within(screen.getByRole('contentinfo')).getByRole('link', {
        name: 'Ingresar',
      }),
    ).toHaveAttribute('href', signInUrl)
  })

  it('marks an explicit choice even when the active locale is selected', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(
      screen.getByRole('button', {
        name: 'Mudar idioma para Português (Brasil)',
      }),
    )

    expect(localStorage.getItem(EXPLICIT_LOCALE_STORAGE_KEY)).toBe('true')
    expect(screen.getByRole('link', { name: 'Começar grátis' })).toHaveAttribute(
      'href',
      addLocaleToUrl(OFFICE_SIGNUP_URL, 'pt-BR'),
    )
  })
})

describe('Landing locale resolution', () => {
  it('normalizes supported browser language variants and falls back to pt-BR', () => {
    expect(normalizeLocale('pt')).toBe('pt-BR')
    expect(normalizeLocale('EN-gb')).toBe('en-US')
    expect(normalizeLocale('es_UY')).toBe('es-AR')
    expect(normalizeLocale('fr-FR')).toBeUndefined()
    expect(resolveInitialLocale(null, 'es-ES')).toBe('es-AR')
    expect(resolveInitialLocale(null, 'fr-FR')).toBe('pt-BR')
  })

  it('gives a persisted supported locale precedence over the browser locale', () => {
    expect(resolveInitialLocale('en-US', 'es-AR')).toBe('en-US')
  })

  it('adds or replaces locale while preserving query parameters and hash fragments', () => {
    expect(
      addLocaleToUrl(
        'https://office.atyno.com.br/login?source=landing&locale=pt-BR#access',
        'en-US',
      ),
    ).toBe(
      'https://office.atyno.com.br/login?source=landing&locale=en-US#access',
    )
  })
})
