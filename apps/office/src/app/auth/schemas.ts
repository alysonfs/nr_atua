import { z } from 'zod'
import type { TFunction } from 'i18next'

/**
 * Schemas de validação de formulário (Zod) para as páginas de autenticação.
 * Usados em conjunto com react-hook-form (@hookform/resolvers/zod).
 */

export const createSignInSchema = (t: TFunction) => z.object({
  email: z.string().min(1, t('auth.validation.emailRequired')).email(t('auth.validation.invalidEmail')),
  password: z.string().min(1, t('auth.validation.passwordRequired')),
})
export type SignInFormValues = z.infer<ReturnType<typeof createSignInSchema>>

export const createSignUpSchema = (t: TFunction) => z
  .object({
    email: z.string().min(1, t('auth.validation.emailRequired')).email(t('auth.validation.invalidEmail')),
    password: z.string().min(8, t('auth.validation.passwordMin')),
    passwordConfirmation: z.string().min(1, t('auth.validation.passwordConfirmationRequired')),
  })
  .refine((data) => data.password === data.passwordConfirmation, {
    message: t('auth.validation.passwordMismatch'),
    path: ['passwordConfirmation'],
  })
export type SignUpFormValues = z.infer<ReturnType<typeof createSignUpSchema>>

export const createConfirmEmailSchema = (t: TFunction) => z.object({
  email: z.string().min(1, t('auth.validation.emailRequired')).email(t('auth.validation.invalidEmail')),
  code: z.string().min(1, t('auth.validation.codeRequired')),
})
export type ConfirmEmailFormValues = z.infer<ReturnType<typeof createConfirmEmailSchema>>
