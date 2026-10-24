import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PdfPreview } from '../../components/PdfPreview'

const mocks = vi.hoisted(() => ({
  getArrayBuffer: vi.fn(),
  getDocument: vi.fn(),
  getPage: vi.fn(),
  render: vi.fn(),
  destroy: vi.fn(),
  cancel: vi.fn(),
}))

vi.mock('../../../../shared/lib/apiClient', () => ({ apiClient: { getArrayBuffer: mocks.getArrayBuffer } }))
vi.mock('pdfjs-dist', () => ({
  GlobalWorkerOptions: {},
  getDocument: mocks.getDocument,
}))

beforeEach(() => {
  vi.clearAllMocks()
  mocks.getArrayBuffer.mockResolvedValue(new ArrayBuffer(8))
  mocks.render.mockReturnValue({ promise: Promise.resolve(), cancel: mocks.cancel })
  mocks.getPage.mockResolvedValue({
    getViewport: ({ scale }: { scale: number }) => ({ width: 600 * scale, height: 800 * scale }),
    render: mocks.render,
  })
  mocks.getDocument.mockReturnValue({
    promise: Promise.resolve({ numPages: 2, getPage: mocks.getPage }),
    destroy: mocks.destroy,
  })
})

describe('PdfPreview', () => {
  it('renders a thumbnail from the authenticated content endpoint without a browser plugin', async () => {
    render(<PdfPreview contentPath="/api/content" thumbnail />)
    await waitFor(() => expect(mocks.render).toHaveBeenCalled())
    expect(mocks.getArrayBuffer).toHaveBeenCalledWith('/api/content', expect.any(AbortSignal))
    expect(screen.getByLabelText('Página 1 do PDF')).toHaveAttribute('width', '112')
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('navigates PDF pages and destroys the worker when closed', async () => {
    const user = userEvent.setup()
    const { unmount } = render(<PdfPreview contentPath="/api/content" />)
    await user.click(await screen.findByRole('button', { name: 'Próxima' }))
    await waitFor(() => expect(mocks.getPage).toHaveBeenCalledWith(2))
    expect(screen.getByRole('button', { name: 'Próxima' })).toBeDisabled()
    unmount()
    expect(mocks.destroy).toHaveBeenCalled()
  })

  it('shows an explicit error when the content request fails', async () => {
    mocks.getArrayBuffer.mockRejectedValue(new Error('unavailable'))
    render(<PdfPreview contentPath="/api/content" />)
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(mocks.getDocument).not.toHaveBeenCalled()
  })

  it('shows an explicit error when the PDF is corrupt', async () => {
    mocks.getDocument.mockReturnValue({ promise: Promise.reject(new Error('invalid PDF')), destroy: mocks.destroy })
    render(<PdfPreview contentPath="/api/content" />)
    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})
