/**
 * Validação de CNPJ no frontend — melhora a experiência do usuário
 * apresentando erro antes do envio, mas NÃO substitui a validação do
 * backend (RF-006.1 / CnpjValidator.cs), que permanece a fonte de
 * verdade sobre formato e unicidade.
 */
export function normalizeCnpj(raw: string): string | null {
  const digits = raw.replace(/\D/g, '')
  if (digits.length !== 14) return null
  if (new Set(digits).size === 1) return null
  if (!hasValidCheckDigits(digits)) return null
  return digits
}

export function isValidCnpj(raw: string): boolean {
  return normalizeCnpj(raw) !== null
}

export function formatCnpj(digits: string): string {
  const clean = digits.replace(/\D/g, '')
  if (clean.length !== 14) return digits
  return clean.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5')
}

function hasValidCheckDigits(digits: string): boolean {
  const numbers = digits.split('').map(Number)

  const firstCheckDigit = calculateCheckDigit(
    numbers.slice(0, 12),
    [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2],
  )
  if (firstCheckDigit !== numbers[12]) return false

  const secondCheckDigit = calculateCheckDigit(
    numbers.slice(0, 13),
    [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2],
  )
  return secondCheckDigit === numbers[13]
}

function calculateCheckDigit(numbers: number[], weights: number[]): number {
  const sum = numbers.reduce((acc, number, index) => acc + number * weights[index], 0)
  const remainder = sum % 11
  return remainder < 2 ? 0 : 11 - remainder
}
