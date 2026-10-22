/**
 * Limite de tamanho aplicado durante o download de mídia inbound (R1 em
 * docs/implementation/RISCOS-triagem-midia-pendentes.md, resolvido em
 * 2026-10-22) — alinhado ao limite aceito pela API
 * (`IngestTriagemChannelMediaUseCase.MaxContentLengthBytes`). A verificação
 * ocorre durante o consumo do stream: ao exceder o limite, o stream é
 * destruído e o download é abortado sem que o binário completo chegue a
 * ser materializado em memória.
 */
export const MAX_INBOUND_MEDIA_BYTES = 16 * 1024 * 1024

export class MediaTooLargeError extends Error {
  readonly limitBytes: number

  constructor(limitBytes: number) {
    super(`Mídia excede o limite de ${limitBytes} bytes permitido para download.`)
    this.name = 'MediaTooLargeError'
    this.limitBytes = limitBytes
  }
}

/**
 * Consome um stream assincronamente, parando e destruindo-o assim que o
 * total de bytes lidos ultrapassa `maxBytes` — nunca acumula o binário
 * inteiro quando ele excede o limite.
 */
export async function collectBufferWithLimit(
  stream: NodeJS.ReadableStream,
  maxBytes: number,
): Promise<Buffer> {
  const chunks: Buffer[] = []
  let total = 0

  try {
    for await (const chunk of stream) {
      const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk as unknown as Uint8Array)
      total += buffer.length
      if (total > maxBytes) {
        throw new MediaTooLargeError(maxBytes)
      }
      chunks.push(buffer)
    }
  } catch (error) {
    const destroyable = stream as unknown as { destroy?: (error?: Error) => void }
    if (typeof destroyable.destroy === 'function') {
      destroyable.destroy()
    }
    throw error
  }

  return Buffer.concat(chunks)
}
