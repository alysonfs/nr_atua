import assert from 'node:assert/strict'
import { Readable } from 'node:stream'
import test from 'node:test'
import { collectBufferWithLimit, MediaTooLargeError } from '../src/media-download-limit.js'

async function* chunksOf(sizes: number[]): AsyncGenerator<Buffer> {
  for (const size of sizes) {
    yield Buffer.alloc(size, 1)
  }
}

test('collectBufferWithLimit concatenates chunks under the limit', async () => {
  const stream = Readable.from(chunksOf([10, 20, 5]))

  const result = await collectBufferWithLimit(stream, 100)

  assert.equal(result.length, 35)
})

test('collectBufferWithLimit aborts and discards without reading the full payload when it exceeds the limit', async () => {
  let yielded = 0
  async function* generate(): AsyncGenerator<Buffer> {
    for (let index = 0; index < 1_000; index += 1) {
      yielded += 1
      yield Buffer.alloc(1_000, 1)
      if (yielded > 5) {
        throw new Error('O gerador não deveria ser consumido além do necessário para detectar o excesso.')
      }
    }
  }
  const stream = Readable.from(generate())

  await assert.rejects(
    () => collectBufferWithLimit(stream, 2_500),
    MediaTooLargeError,
  )
  assert.ok(yielded <= 5, `esperava poucos chunks consumidos antes do abort, obteve ${yielded}`)
})

test('MediaTooLargeError exposes the configured limit', async () => {
  const stream = Readable.from(chunksOf([10]))

  try {
    await collectBufferWithLimit(stream, 5)
    assert.fail('deveria ter lançado MediaTooLargeError')
  } catch (error) {
    assert.ok(error instanceof MediaTooLargeError)
    assert.equal((error as MediaTooLargeError).limitBytes, 5)
  }
})
