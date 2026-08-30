import { describe, expect, it } from 'vitest'
import { formatCnpj, isValidCnpj, normalizeCnpj } from '../../lib/cnpj'

// CNPJ válido conhecido (dígitos verificadores corretos)
const VALID_CNPJ = '11444777000161'

describe('cnpj', () => {
  it('normaliza um CNPJ válido removendo máscara', () => {
    expect(normalizeCnpj('11.444.777/0001-61')).toBe(VALID_CNPJ)
  })

  it('rejeita CNPJ com dígitos verificadores incorretos', () => {
    expect(normalizeCnpj('11.444.777/0001-62')).toBeNull()
  })

  it('rejeita CNPJ com todos os dígitos iguais', () => {
    expect(normalizeCnpj('11111111111111')).toBeNull()
  })

  it('rejeita CNPJ com quantidade de dígitos incorreta', () => {
    expect(normalizeCnpj('123')).toBeNull()
  })

  it('isValidCnpj reflete o resultado da normalização', () => {
    expect(isValidCnpj(VALID_CNPJ)).toBe(true)
    expect(isValidCnpj('00000000000000')).toBe(false)
  })

  it('formata um CNPJ de 14 dígitos com máscara', () => {
    expect(formatCnpj(VALID_CNPJ)).toBe('11.444.777/0001-61')
  })
})
