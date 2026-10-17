import type { SafeLogger } from './contracts.js'

function write(
  level: 'info' | 'warn' | 'error',
  event: string,
  fields: Record<string, string | number | boolean> = {},
): void {
  const record = JSON.stringify({
    timestamp: new Date().toISOString(),
    level,
    event,
    ...fields,
  })

  if (level === 'error') {
    console.error(record)
  } else if (level === 'warn') {
    console.warn(record)
  } else {
    console.info(record)
  }
}

export const consoleLogger: SafeLogger = {
  info: (event, fields) => write('info', event, fields),
  warn: (event, fields) => write('warn', event, fields),
  error: (event, fields) => write('error', event, fields),
}
