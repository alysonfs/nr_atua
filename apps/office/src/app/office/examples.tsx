/**
 * Example: Integration of Trial and Timezone components
 * 
 * This file shows how to use the RF-003 (Trial) and RF-004 (Timezone)
 * components together in a real application.
 */

import { useState } from 'react'
import {
  TrialBadge,
  TrialCard,
  TrialExpiryAlert,
} from './components/Trial'
import {
  TimezonePicker,
  TimezonePreference,
} from './components/Settings'
import { useUserTrial, useTimezone } from './hooks'
import { getSuggestedTimezone } from './lib/timezone'

/**
 * Example: Header with Trial Badge
 * 
 * Shows compact Trial status in header
 */
export function ExampleHeaderWithTrial() {
  return (
    <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-4">
      <h1 className="text-xl font-bold">ATUA Office</h1>
      <div className="flex items-center gap-4">
        {/* Trial badge (compact version) */}
        <TrialBadge compact={true} />
        {/* User avatar, etc */}
      </div>
    </header>
  )
}

/**
 * Example: Dashboard with Trial Alert and Card
 * 
 * Shows Trial status prominently when about to expire
 */
export function ExampleDashboardWithTrial() {
  const { summary } = useUserTrial()
  const [alertClosed, setAlertClosed] = useState(false)

  return (
    <div className="space-y-6 p-6">
      {/* Show alert if trial is about to expire */}
      {summary?.isAboutToExpire && !alertClosed && (
        <TrialExpiryAlert
          onClose={() => setAlertClosed(true)}
          onActionClick={() => {
            // Navigate to upgrade page
            console.log('Opening upgrade flow...')
          }}
        />
      )}

      {/* Show trial card on dashboard */}
      {summary?.isAboutToExpire && (
        <TrialCard
          showCTA={true}
          onUpgradeClick={() => {
            // Navigate to upgrade page
            console.log('Opening upgrade flow...')
          }}
        />
      )}

      {/* Other dashboard content */}
      <div className="grid grid-cols-1 gap-6">
        {/* Your dashboard content here */}
      </div>
    </div>
  )
}

/**
 * Example: Account Preferences Page
 * 
 * Shows Trial info and Timezone preference sections
 */
export function ExampleAccountPreferences() {
  const { trial, summary } = useUserTrial()

  return (
    <div className="space-y-8 p-6">
      <h1 className="text-2xl font-bold">Preferências da Conta</h1>

      {/* Trial Section */}
      <section>
        <h2 className="mb-4 text-lg font-semibold">Status do Trial</h2>
        {trial && summary && (
          <TrialCard
            showCTA={true}
            onUpgradeClick={() => {
              console.log('Opening upgrade flow...')
            }}
          />
        )}
      </section>

      {/* Timezone Section */}
      <section className="border-t border-slate-200 pt-8">
        <h2 className="mb-4 text-lg font-semibold">Configurações Regionais</h2>
        <TimezonePreference
          onTimezoneUpdated={(newTz) => {
            console.log('Timezone updated to:', newTz)
          }}
        />
      </section>

      {/* Other preferences sections */}
      <section className="border-t border-slate-200 pt-8">
        <h2 className="mb-4 text-lg font-semibold">Notificações</h2>
        {/* Notification preferences */}
      </section>
    </div>
  )
}

/**
 * Example: Modal for Timezone Selection
 * 
 * Standalone timezone picker in a modal
 */
export function ExampleTimezoneSelectorModal() {
  const { currentTimezone, updateTimezone } = useTimezone()
  const [isOpen, setIsOpen] = useState(false)
  const suggestedTz = getSuggestedTimezone()

  return (
    <>
      <button
        onClick={() => setIsOpen(true)}
        className="rounded-lg bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700"
      >
        Abrir Seletor de Fuso Horário
      </button>

      {isOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="mx-4 max-h-[80vh] w-full max-w-md overflow-y-auto rounded-lg bg-white shadow-lg">
            <TimezonePicker
              currentTimezone={currentTimezone}
              suggestedTimezone={suggestedTz}
              onSelect={async (newTz) => {
                try {
                  await updateTimezone(newTz)
                  setIsOpen(false)
                } catch (error) {
                  console.error('Failed to update timezone:', error)
                }
              }}
              isModal={true}
              onClose={() => setIsOpen(false)}
            />
          </div>
        </div>
      )}
    </>
  )
}

/**
 * Example: Complete Onboarding Flow
 * 
 * After email confirmation, show Trial info and timezone selection
 */
export function ExampleOnboardingAfterEmailConfirmation() {
  const { trial } = useUserTrial()
  const { currentTimezone, updateTimezone } = useTimezone()
  const [step, setStep] = useState<'timezone' | 'complete'>('timezone')

  if (step === 'timezone') {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-blue-50 to-slate-100">
        <div className="w-full max-w-md rounded-lg bg-white p-8 shadow-lg">
          <h1 className="mb-2 text-2xl font-bold text-slate-900">Bem-vindo ao ATUA!</h1>
          <p className="mb-6 text-slate-600">
            Seu Trial de 7 dias começou. Primeiro, vamos definir seu fuso horário.
          </p>

          <div className="mb-6 rounded-lg bg-emerald-50 p-4">
            <p className="text-sm font-semibold text-emerald-900">
              ✓ Trial ativado até {trial ? new Date(trial.expiresAtUtc).toLocaleDateString('pt-BR') : '...'}
            </p>
          </div>

          <div className="mb-6">
            <TimezonePicker
              currentTimezone={currentTimezone}
              onSelect={async (newTz) => {
                try {
                  await updateTimezone(newTz)
                  setStep('complete')
                } catch (error) {
                  console.error('Failed to update timezone:', error)
                }
              }}
            />
          </div>

          <button
            onClick={() => setStep('complete')}
            className="w-full rounded-lg bg-blue-600 px-4 py-2 font-semibold text-white hover:bg-blue-700"
          >
            Prosseguir
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-blue-50 to-slate-100">
      <div className="w-full max-w-md rounded-lg bg-white p-8 shadow-lg">
        <h1 className="mb-4 text-2xl font-bold text-slate-900">Tudo pronto!</h1>
        <p className="mb-6 text-slate-600">
          Seu fuso horário foi configurado. Agora você pode começar a usar o ATUA.
        </p>

        <TrialCard
          className="mb-6"
          showCTA={false}
        />

        <a
          href="/office/dashboard"
          className="block rounded-lg bg-blue-600 px-4 py-2 text-center font-semibold text-white hover:bg-blue-700"
        >
          Ir para o Office
        </a>
      </div>
    </div>
  )
}
