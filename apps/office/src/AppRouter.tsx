import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { ToastContainer } from 'react-toastify'
import 'react-toastify/dist/ReactToastify.css'
import { AuthProvider } from './app/auth/AuthContext'
import { ProtectedRoute } from './app/auth/ProtectedRoute'
import { PublicOnlyRoute } from './app/auth/PublicOnlyRoute'
import { SignInPage } from './app/auth/pages/SignInPage'
import { SignUpPage } from './app/auth/pages/SignUpPage'
import { ConfirmEmailPage } from './app/auth/pages/ConfirmEmailPage'
import App from './App'

export function AppRouter() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <ToastContainer position="top-right" autoClose={5000} newestOnTop />
        <Routes>
          {/* Rotas públicas */}
          <Route element={<PublicOnlyRoute />}>
            <Route path="/login" element={<SignInPage />} />
            <Route path="/cadastro" element={<SignUpPage />} />
            <Route path="/confirmar-email" element={<ConfirmEmailPage />} />
          </Route>

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
