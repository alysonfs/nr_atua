import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './i18n.ts'
import './index.css'
import { AppRouter } from './AppRouter.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppRouter />
  </StrictMode>,
)
