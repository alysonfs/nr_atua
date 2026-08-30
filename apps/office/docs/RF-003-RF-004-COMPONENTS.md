# Componentes de Trial (RF-003) e Timezone (RF-004)

## Visão Geral

Este documento descreve como usar os componentes e hooks implementados para **RF-003 (Trial)** e **RF-004 (Timezone)** na aplicação Office.

### RF-003: Plano Trial

O usuário recebe um trial de 7 dias após confirmar seu e-mail. O status do trial deve ser exibido em:

- **Header**: Badge compacto mostrando status
- **Dashboard**: Card informativo quando < 2 dias restantes
- **Topo da página**: Alert quando < 2 dias restantes

### RF-004: Preferência de Fuso Horário

Cada usuário possui um fuso horário em formato IANA. Datas são armazenadas em UTC e exibidas no fuso horário do usuário.

---

## Componentes

### Trial Components

#### 1. TrialBadge

Exibe o status do trial em formato compacto.

**Uso:**

```tsx
import { TrialBadge } from '@/app/office/components/Trial'

// Compact (para header)
<TrialBadge compact={true} />

// Expanded (para sidebar/dashboard)
<TrialBadge />

// Com classe customizada
<TrialBadge className="custom-class" />
```

**Props:**

- `compact?: boolean` - Modo compacto para header (default: false)
- `className?: string` - Classe CSS customizada

**Localidades:**

- Header (lado direito, próximo ao avatar)
- Sidebar
- Dashboard

**Cores:**

- Verde quando ativo e > 2 dias
- Amarelo quando 1-2 dias
- Vermelho quando < 1 dia ou expirado

---

#### 2. TrialCard

Exibe informações detalhadas do trial em um card.

**Uso:**

```tsx
import { TrialCard } from '@/app/office/components/Trial'

// Básico
<TrialCard />

// Com CTA para upgrade
<TrialCard 
  showCTA={true} 
  onUpgradeClick={() => navigate('/upgrade')}
/>

// Com classe customizada
<TrialCard className="mb-6" />
```

**Props:**

- `className?: string` - Classe CSS customizada
- `showCTA?: boolean` - Mostrar botão de upgrade (default: false)
- `onUpgradeClick?: () => void` - Callback ao clicar em upgrade

**Informações Exibidas:**

- Status do trial (Ativo, Expirado, Expira em breve)
- Dias restantes
- Horas restantes
- Data de ativação
- Data de expiração
- Mensagens de alerta quando < 2 dias

---

#### 3. TrialExpiryAlert

Banner de alerta quando o trial está prestes a expirar (< 2 dias).

**Uso:**

```tsx
import { TrialExpiryAlert } from '@/app/office/components/Trial'

// Básico
<TrialExpiryAlert />

// Com callbacks
<TrialExpiryAlert 
  onClose={() => setAlertClosed(true)}
  onActionClick={() => navigate('/upgrade')}
/>

// Sem botão de fechar
<TrialExpiryAlert showClose={false} />
```

**Props:**

- `className?: string` - Classe CSS customizada
- `showClose?: boolean` - Mostrar botão de fechar (default: true)
- `onClose?: () => void` - Callback ao fechar
- `onActionClick?: () => void` - Callback ao clicar em upgrade

**Nota:** Este componente retorna `null` quando o trial está ativo e > 2 dias.

---

### Timezone Components

#### 1. TimezonePicker

Seletor de fuso horário com pesquisa e sugestões.

**Uso:**

```tsx
import { TimezonePicker } from '@/app/office/components/Settings'
import { getSuggestedTimezone } from '@/app/office/lib/timezone'

const [selectedTz, setSelectedTz] = useState('America/Sao_Paulo')

<TimezonePicker
  currentTimezone={selectedTz}
  suggestedTimezone={getSuggestedTimezone()}
  onSelect={(tz) => setSelectedTz(tz)}
/>

// Modal mode
<TimezonePicker
  currentTimezone={selectedTz}
  suggestedTimezone={getSuggestedTimezone()}
  onSelect={(tz) => setSelectedTz(tz)}
  isModal={true}
  onClose={() => setIsOpen(false)}
/>
```

**Props:**

- `currentTimezone: string` - Fuso horário atual (IANA format)
- `suggestedTimezone?: string` - Fuso horário sugerido (IANA format)
- `onSelect: (timezone: string) => void` - Callback ao selecionar
- `className?: string` - Classe CSS customizada
- `isModal?: boolean` - Modo modal com overlay (default: false)
- `onClose?: () => void` - Callback para fechar modal

**Recursos:**

- Pesquisa por nome ou código
- Seção de sugeridos
- Seção de timezone atual
- Lista de populares (quando sem pesquisa)
- Exibe UTC offset atual
- Indicador "Atual" para timezone selecionado

---

#### 2. TimezonePreference

Componente para preferências de timezone em settings.

**Uso:**

```tsx
import { TimezonePreference } from '@/app/office/components/Settings'

// Básico (com card)
<TimezonePreference
  onTimezoneUpdated={(newTz) => {
    console.log('Timezone atualizado:', newTz)
  }}
/>

// Sem card container
<TimezonePreference
  showCard={false}
  onTimezoneUpdated={(newTz) => {
    console.log('Timezone atualizado:', newTz)
  }}
/>
```

**Props:**

- `className?: string` - Classe CSS customizada
- `showCard?: boolean` - Renderizar dentro de um card (default: true)
- `onTimezoneUpdated?: (tz: string) => void` - Callback após atualizar

**Recursos:**

- Mostra timezone atual com UTC offset
- Botão para abrir seletor de fuso horário
- Estado de carregamento durante atualização
- Mensagens de erro se atualizar falhar
- Modal para seleção de fuso horário

---

## Hooks

### Trial Hooks

#### 1. useUserTrial()

Hook principal para gerenciar o status do trial do usuário.

**Uso:**

```tsx
import { useUserTrial } from '@/app/office/hooks'

const { trial, summary, isLoading, isError, refetch } = useUserTrial()

if (isLoading) return <div>Carregando...</div>
if (!trial) return null

return (
  <div>
    <p>Status: {summary.isActive ? 'Ativo' : 'Expirado'}</p>
    <p>Dias restantes: {summary.daysRemaining}</p>
  </div>
)
```

**Retorno:**

```typescript
{
  trial: TrialDTO | null           // Dados do trial
  summary: TrialSummary | null     // Resumo calculado
  isLoading: boolean               // Carregando
  isError: boolean                 // Erro ao carregar
  refetch: () => void              // Recarregar dados
}
```

**TrialSummary:**

```typescript
{
  isActive: boolean        // Trial está ativo
  isExpired: boolean       // Trial expirou
  daysRemaining: number    // Dias restantes
  hoursRemaining: number   // Horas restantes
  isAboutToExpire: boolean // < 2 dias
  isUrgent: boolean        // < 4 horas
}
```

---

#### 2. Hooks Auxiliares

```tsx
import {
  useIsTrialActive,      // Retorna boolean
  useIsTrialExpiring,    // Retorna boolean (< 2 dias)
  useIsTrialUrgent,      // Retorna boolean (< 4 horas)
  useTrialDaysRemaining, // Retorna número
  useTrialHoursRemaining // Retorna número
} from '@/app/office/hooks'

// Exemplos de uso
if (useIsTrialActive()) {
  // Mostrar features limitadas ao trial
}

if (useIsTrialExpiring()) {
  // Mostrar alerta de expiração
}

const daysLeft = useTrialDaysRemaining()
```

---

### Timezone Hooks

#### 1. useTimezone()

Hook principal para gerenciar timezone do usuário.

**Uso:**

```tsx
import { useTimezone } from '@/app/office/hooks'

const {
  currentTimezone,      // String IANA format
  formatDateLocal,      // Função para formatar datas
  updateTimezone,       // Função para atualizar
  resetToSuggested,     // Reset para sugerido
  getCurrentTimezone,   // Getter
  isLoading,            // Estado de carregamento
  error                 // Mensagem de erro
} = useTimezone()

// Formatar data
const formatted = formatDateLocal(utcDate, 'dd/MM/yyyy HH:mm')

// Atualizar timezone
await updateTimezone('America/New_York')
```

**Recursos:**

- Salva preference em localStorage
- Sugere timezone baseado em idioma do navegador
- Valida timezone antes de atualizar
- Simula chamada à API (será integrada)

---

#### 2. Hooks Auxiliares

```tsx
import {
  useFormatDateInTimezone, // Retorna função formatadora
  useCurrentTimezone,      // Retorna string
  useUpdateTimezone        // Retorna { updateTimezone, isLoading, error }
} from '@/app/office/hooks'

// Exemplo
const formatDate = useFormatDateInTimezone()
const formatted = formatDate(utcDate)
```

---

## Integração com Backend

### Endpoints Necessários

**GET /api/users/me/trial**

Retorna informações do trial do usuário autenticado.

```json
Response:
{
  "trialId": "trial-123",
  "expiresAtUtc": "2026-09-05T23:59:59Z",
  "activatedAtUtc": "2026-08-29T13:00:00Z",
  "status": "active",
  "daysRemaining": 7
}
```

**PUT /api/users/me/timezone**

Atualiza o timezone do usuário.

```json
Request:
{
  "timezone": "America/Sao_Paulo"
}

Response:
{
  "timezone": "America/Sao_Paulo",
  "updatedAt": "2026-08-29T13:14:45Z"
}
```

### Implementação de Integração

Quando os endpoints estiverem prontos, atualizar:

1. `hooks/useUserTrial.ts` - Adicionar chamada à API
2. `hooks/useTimezone.ts` - Adicionar chamadas à API
3. Remover dados mockados

---

## Testes

### Rodando os testes

```bash
# Rodar testes de Trial
npm test Trial.test.tsx

# Rodar testes de Timezone
npm test timezone.test.ts

# Rodar todos os testes
npm test
```

---

## Referências

- [RF-003: Plano Trial](../../docs/requirements/mvp-onboarding-coleta-e-supervisao.md)
- [RF-004: Preferência de Fuso Horário](../../docs/requirements/mvp-onboarding-coleta-e-supervisao.md)
- [ADR-005: Tenancy e Memberships](../../docs/decisions/ADR-005-tenancy-memberships-e-integracoes.md)
- [Design System](../../docs/architecture/design-system.md)
