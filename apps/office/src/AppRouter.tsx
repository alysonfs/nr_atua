import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { ToastContainer } from 'react-toastify'
import 'react-toastify/dist/ReactToastify.css'
import { AuthProvider } from './app/auth/AuthContext'
import { ProtectedRoute } from './app/auth/ProtectedRoute'
import { PublicOnlyRoute } from './app/auth/PublicOnlyRoute'
import { SignInPage } from './app/auth/pages/SignInPage'
import { SignUpPage } from './app/auth/pages/SignUpPage'
import { ConfirmEmailPage } from './app/auth/pages/ConfirmEmailPage'
import { AppLayout } from './app/office/components/Layout'
import { DashboardPage } from './app/dashboard/pages/DashboardPage'
import { ServiceOrdersPage } from './app/service-orders/pages/ServiceOrdersPage'
import { WorkOrderDetailPage } from './app/dashboard/pages/WorkOrderDetailPage'
import { SettingsPage } from './app/office/pages/SettingsPage'
import { TriagePoolPage } from './app/triage-pool/pages/TriagePoolPage'
import { TriagePoolDetailPage } from './app/triage-pool/pages/TriagePoolDetailPage'

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
            <Route element={<AppLayout />}>
              <Route path="/home" element={<DashboardPage />} />
              <Route path="/service-orders" element={<ServiceOrdersPage />} />
              <Route path="/work-orders/:workOrderId" element={<WorkOrderDetailPage />} />
              <Route path="/triage-pool" element={<TriagePoolPage />} />
              <Route path="/triage-pool/:triagemId" element={<TriagePoolDetailPage />} />
              <Route path="/settings" element={<SettingsPage />} />
            </Route>
          </Route>

          {/* Home pós-login é o dashboard */}
          <Route path="/" element={<Navigate to="/home" replace />} />

          {/* Fallback */}
          <Route path="*" element={<Navigate to="/home" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}

