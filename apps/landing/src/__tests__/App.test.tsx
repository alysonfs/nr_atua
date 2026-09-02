import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from '../App'
import { OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL, SUPPORT_EMAIL } from '../constants'

describe('Landing page', () => {
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
})
