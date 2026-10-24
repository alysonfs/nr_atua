import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { PDFDocumentProxy, PDFDocumentLoadingTask } from 'pdfjs-dist'
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'
import { apiClient } from '../../../shared/lib/apiClient'

interface PdfPreviewProps {
  contentPath?: string
  thumbnail?: boolean
  scale?: number
}

export function PdfPreview({ contentPath, thumbnail = false, scale = 1 }: PdfPreviewProps) {
  const { t } = useTranslation()
  const canvas = useRef<HTMLCanvasElement>(null)
  const [document, setDocument] = useState<PDFDocumentProxy | null>(null)
  const [pageNumber, setPageNumber] = useState(1)
  const [failed, setFailed] = useState(false)
  const [rendered, setRendered] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    let loadingTask: PDFDocumentLoadingTask | undefined

    void (async () => {
      try {
        const data = contentPath
          ? await apiClient.getArrayBuffer(contentPath, controller.signal)
          : null
        if (controller.signal.aborted) return
        if (!data) throw new Error('attachment_content_unavailable')
        const { getDocument, GlobalWorkerOptions } = await import('pdfjs-dist')
        if (controller.signal.aborted) return
        GlobalWorkerOptions.workerSrc = workerUrl
        loadingTask = getDocument({ data })
        const pdf = await loadingTask.promise
        if (!controller.signal.aborted) setDocument(pdf)
      } catch {
        if (!controller.signal.aborted) setFailed(true)
      }
    })()

    return () => {
      controller.abort()
      void loadingTask?.destroy()
    }
  }, [contentPath])

  useEffect(() => {
    if (!document) return
    let cancelled = false
    let renderTask: ReturnType<Awaited<ReturnType<PDFDocumentProxy['getPage']>>['render']> | undefined
    void (async () => {
      try {
        setRendered(false)
        const pdfPage = await document.getPage(pageNumber)
        if (cancelled || !canvas.current) return
        const original = pdfPage.getViewport({ scale: 1 })
        const width = thumbnail ? 112 : 650 * scale
        const viewport = pdfPage.getViewport({ scale: width / original.width })
        const target = canvas.current
        target.width = viewport.width
        target.height = viewport.height
        renderTask = pdfPage.render({ canvas: target, viewport })
        await renderTask.promise
        if (!cancelled) setRendered(true)
      } catch {
        if (!cancelled) setFailed(true)
      }
    })()

    return () => {
      cancelled = true
      renderTask?.cancel()
    }
  }, [document, pageNumber, scale, thumbnail])

  if (failed) {
    return <p role="alert" className="p-2 text-xs text-amber-800">{t('triagePoolDetail.attachments.loadError')}</p>
  }

  return (
    <div className={thumbnail ? 'w-28' : 'flex h-full min-h-0 flex-col gap-2'}>
      {!rendered && <p role="status" className="p-2 text-xs text-slate-500">{t('triagePoolDetail.attachments.preview.loading')}</p>}
      <div className={thumbnail ? 'h-28 overflow-hidden rounded-md border border-slate-200' : 'min-h-0 flex-1 overflow-auto'}>
        <canvas
          ref={canvas}
          aria-label={t('triagePoolDetail.attachments.preview.pdfPage', { page: pageNumber })}
          className={scale === 1 ? 'mx-auto max-w-full' : 'mx-auto'}
          style={{ display: rendered ? 'block' : 'none' }}
        />
      </div>
      {!thumbnail && document && document.numPages > 1 && (
        <div className="flex items-center justify-center gap-3 text-sm">
          <button type="button" disabled={pageNumber === 1} onClick={() => setPageNumber(page => page - 1)}>
            {t('triagePoolDetail.attachments.preview.previous')}
          </button>
          <span>{t('triagePoolDetail.attachments.preview.counter', { current: pageNumber, total: document.numPages })}</span>
          <button type="button" disabled={pageNumber === document.numPages} onClick={() => setPageNumber(page => page + 1)}>
            {t('triagePoolDetail.attachments.preview.next')}
          </button>
        </div>
      )}
    </div>
  )
}
