/**
 * URLs das rotas de autenticação do Office, publicado no mesmo bucket S3
 * sob o prefixo `/office/` (ver `apps/office/vite.config.ts`). A landing é
 * publicada sob `/landing/`, portanto os links precisam ser absolutos.
 *
 * Navegação entre a landing e o Office é feita por reload completo de
 * página (`<a>` normal), pois são duas SPAs independentes.
 */
export const OFFICE_SIGNUP_URL = '/office/cadastro'
export const OFFICE_SIGNIN_URL = '/office/login'
export const SUPPORT_EMAIL = 'suporte@atua.com.br'
