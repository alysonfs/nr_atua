import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider } from './app/auth/AuthContext'
import { ProtectedRoute } from './app/auth/ProtectedRoute'
import { SignInPage } from './app/auth/pages/SignInPage'
import { SignUpPage } from './app/auth/pages/SignUpPage'
import { ConfirmEmailPage } from './app/auth/pages/ConfirmEmailPage'
import App from './App'

export function AppRouter() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          {/* Rotas públicas */}
          <Route path="/login" element={<SignInPage />} />
          <Route path="/cadastro" element={<SignUpPage />} />
          <Route path="/confirmar-email" element={<ConfirmEmailPage />} />

          {/* Rotas protegidas */}
          <Route element={<ProtectedRoute />}>
            <Route path="/" element={<App />} />
          </Route>

          {/* Fallback */}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}
