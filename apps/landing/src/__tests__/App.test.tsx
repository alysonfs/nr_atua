import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from '../App'
import { OFFICE_SIGNIN_URL, OFFICE_SIGNUP_URL, SUPPORT_EMAIL } from '../constants'

describe('Landing page', () => {
  it('renders the hero headline, subheadline and primary CTAs', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', {
        level: 1,
        name: 'A complexidade fica dentro do ATUA. A simplicidade fica para você.',
      }),
    ).toBeInTheDocument()

    expect(
      screen.getByText(/ATUA conecta a operação da sua assistência técnica ao iService/),
    ).toBeInTheDocument()

    const heroCtas = screen.getAllByRole('link', { name: 'Começar grátis' })
    expect(heroCtas[0]).toHaveAttribute('href', OFFICE_SIGNUP_URL)

    const heroSignIn = screen.getAllByRole('link', { name: 'Entrar' })
    expect(heroSignIn[0]).toHaveAttribute('href', OFFICE_SIGNIN_URL)
  })

  it('renders section 1 - "Uma plataforma para toda a operação"', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', { name: 'Uma plataforma para toda a operação' }),
    ).toBeInTheDocument()
    expect(screen.getByText(/Conecte sua conta ao iService, ative o Agente Coletor/)).toBeInTheDocument()
  })

  it('renders section 2 - público-alvo', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', {
        name: 'Feito para assistências técnicas e empresas de serviços técnicos',
      }),
    ).toBeInTheDocument()
  })

  it('renders section 3 - argumento de confiança "O ATUA observa. Nunca interfere."', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', { name: 'O ATUA observa. Nunca interfere.' }),
    ).toBeInTheDocument()
    expect(
      screen.getByText(/O Agente Coletor do ATUA opera exclusivamente em modo somente leitura/),
    ).toBeInTheDocument()
  })

  it('renders section 4 - "Comece em três passos" com os três passos numerados', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Comece em três passos' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Crie sua conta' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Conecte ao iService' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Ative o Agente Coletor' })).toBeInTheDocument()
  })

  it('renders section 5 - CTA final com os links corretos', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Pronto para conectar sua operação?' })).toBeInTheDocument()

    const createAccount = screen.getByRole('link', { name: 'Criar conta grátis' })
    expect(createAccount).toHaveAttribute('href', OFFICE_SIGNUP_URL)

    const signIn = screen.getByRole('link', { name: 'Já tenho conta — Entrar' })
    expect(signIn).toHaveAttribute('href', OFFICE_SIGNIN_URL)
  })

  it('renders the footer with tagline, links, dynamic year and MVP notice', () => {
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
})
