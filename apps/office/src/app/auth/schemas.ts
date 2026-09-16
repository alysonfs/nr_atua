import { z } from 'zod'

/**
 * Schemas de validação de formulário (Zod) para as páginas de autenticação.
 * Usados em conjunto com react-hook-form (@hookform/resolvers/zod).
 */

export const signInSchema = z.object({
  email: z.string().min(1, 'Informe seu e-mail.').email('E-mail inválido.'),
  password: z.string().min(1, 'Informe sua senha.'),
})
export type SignInFormValues = z.infer<typeof signInSchema>

export const signUpSchema = z
  .object({
    email: z.string().min(1, 'Informe seu e-mail.').email('E-mail inválido.'),
    password: z.string().min(8, 'A senha deve ter no mínimo 8 caracteres.'),
    passwordConfirmation: z.string().min(1, 'Confirme sua senha.'),
  })
  .refine((data) => data.password === data.passwordConfirmation, {
    message: 'As senhas não coincidem.',
    path: ['passwordConfirmation'],
  })
export type SignUpFormValues = z.infer<typeof signUpSchema>

export const confirmEmailSchema = z.object({
  email: z.string().min(1, 'Informe seu e-mail.').email('E-mail inválido.'),
  code: z.string().min(1, 'Informe o código de confirmação.'),
})
export type ConfirmEmailFormValues = z.infer<typeof confirmEmailSchema>
