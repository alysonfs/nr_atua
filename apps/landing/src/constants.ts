/**
 * URLs das rotas de autenticação da área da conta (Office), publicada em
 * subdomínio próprio (`office.atyno.com.br`, ver `apps/office/vite.config.ts`
 * e `apps/office/src/AppRouter.tsx`). A landing é publicada na raiz do
 * domínio (`atyno.com.br`), portanto os links precisam ser absolutos e
 * cross-origin.
 *
 * Em desenvolvimento local, aponta para o servidor de dev do Office
 * (porta fixa 5175, ver `apps/office/vite.config.ts`) para permitir o
 * clique real entre as duas SPAs sem depender do domínio de produção.
 *
 * Navegação entre a landing e a área da conta é feita por reload completo de
 * página (`<a>` normal), pois são duas SPAs independentes.
 */
const OFFICE_ORIGIN = import.meta.env.DEV ? 'http://localhost:5175' : 'https://office.atyno.com.br'

export const OFFICE_SIGNUP_URL = `${OFFICE_ORIGIN}/cadastro`
export const OFFICE_SIGNIN_URL = `${OFFICE_ORIGIN}/login`
export const SUPPORT_EMAIL = 'suporte@atua.com.br'
