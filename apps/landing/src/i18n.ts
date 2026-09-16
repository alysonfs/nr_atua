import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'

export const SUPPORTED_LOCALES = ['pt-BR', 'en-US', 'es-AR'] as const
export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number]

export const DEFAULT_LOCALE: SupportedLocale = 'pt-BR'
export const LOCALE_STORAGE_KEY = 'atua.locale'
export const EXPLICIT_LOCALE_STORAGE_KEY = 'atua.landing.localeExplicit'

export const languages: ReadonlyArray<{
  locale: SupportedLocale
  flag: string
  nameKey: string
}> = [
  { locale: 'pt-BR', flag: '🇧🇷', nameKey: 'languageSelector.languages.pt-BR' },
  { locale: 'en-US', flag: '🇺🇸', nameKey: 'languageSelector.languages.en-US' },
  { locale: 'es-AR', flag: '🇦🇷', nameKey: 'languageSelector.languages.es-AR' },
]

const resources = {
  'pt-BR': {
    translation: {
      metadata: {
        title: 'ATUA — Plataforma operacional para empresas de serviços técnicos',
        description:
          'ATUA unifica dados e visibilidade operacional para empresas de serviços técnicos em uma plataforma para vários provedores.',
      },
      common: {
        logoAlt: 'ATUA',
        actions: {
          signUp: 'Começar grátis',
          signIn: 'Entrar',
          seeHowItWorks: 'Ver como funciona',
          createAccount: 'Criar conta',
          createAccountFree: 'Criar conta grátis',
          existingAccountSignIn: 'Já tenho conta — Entrar',
        },
      },
      languageSelector: {
        label: 'Selecionar idioma',
        optionLabel: 'Mudar idioma para {{language}}',
        languages: {
          'pt-BR': 'Português (Brasil)',
          'en-US': 'English (United States)',
          'es-AR': 'Español (Argentina)',
        },
      },
      hero: {
        eyebrow: 'Conectividade técnica unificada',
        headline:
          'Sua operação técnica, unificada em uma plataforma para vários provedores.',
        subheadline:
          'O ATUA organiza dados, acompanhamento e visibilidade operacional para empresas de serviços técnicos — começando pelo conector iService e preparado para evoluir com novos provedores.',
        badgesLabel: 'Características da plataforma',
        badges: {
          unifiedPlatform: 'Plataforma única',
          connectedData: 'Dados conectados',
          multipleProviders: 'Vários provedores',
        },
        diagram: {
          title: 'Centro operacional',
          inputLabel: 'Entrada de dados',
          inputValue: 'Múltiplos provedores',
          layerLabel: 'Camada ATUA',
          layerValue: 'Dados unificados',
          outputLabel: 'Saída para sua equipe',
          outputValue: 'Visão operacional',
        },
      },
      platform: {
        eyebrow: 'O que muda na prática',
        title: 'Uma plataforma para toda a operação',
        description:
          'O ATUA é a camada operacional para empresas de serviços técnicos que precisam transformar fontes dispersas em dados organizados, leitura clara e contexto compartilhado.',
        cards: {
          organizedData: {
            title: 'Dados organizados',
            description:
              'Transforme leituras operacionais em uma base interna mais clara.',
          },
          lessFriction: {
            title: 'Menos atrito',
            description:
              'Reduza a troca entre sistemas, planilhas e consultas dispersas.',
          },
          multipleProviders: {
            title: 'Base para vários provedores',
            description:
              'Comece pelo conector inicial sem limitar a evolução da plataforma.',
          },
          continuousGrowth: {
            title: 'Crescimento contínuo',
            description:
              'Prepare a operação para novos provedores e automações graduais.',
          },
        },
      },
      audience: {
        eyebrow: 'Conectores e provedores',
        title: 'Comece pelos provedores que sua operação já usa',
        description:
          'O ATUA nasce aberto a múltiplas fontes. iService é o conector inicial, mas não é o limite da plataforma.',
        connectorLabel: 'Conector',
        providers: {
          iservice: {
            name: 'iService',
            status: 'Conector inicial',
          },
          new: {
            name: 'Novos provedores',
            status: 'Planejado',
          },
          future: {
            name: 'Provedores futuros',
            status: 'Base expansível',
          },
        },
      },
      trust: {
        eyebrow: 'Evolução por etapas',
        title: 'Primeiro leitura confiável. Depois automação com controle.',
        description:
          'Nesta fase, o ATUA coleta e organiza informações sem executar ações operacionais no provedor. Essa base dá visibilidade agora e prepara o caminho para automatizar passos futuros com regras claras, autorização e rastreabilidade.',
        cards: {
          reading: {
            title: 'Fase de leitura',
            description:
              'A etapa atual prioriza coleta e organização dos dados do provedor.',
          },
          traceability: {
            title: 'Rastreabilidade',
            description:
              'Comandos, tentativas e resultados ficam registrados para acompanhamento.',
          },
          gradualAutomation: {
            title: 'Automação gradual',
            description:
              'Novas ações entram uma por vez, quando houver controle operacional suficiente.',
          },
        },
      },
      steps: {
        title: 'Comece em três passos',
        items: {
          account: {
            title: 'Crie sua conta',
            description:
              'Cadastre-se com e-mail e senha. Confirme o e-mail e seu período de avaliação começa automaticamente — sem cartão de crédito.',
          },
          sources: {
            title: 'Conecte suas fontes',
            description:
              'Na área da sua conta, configure o conector inicial disponível para sua operação.',
          },
          collector: {
            title: 'Ative o Agente Coletor',
            description:
              'Com a configuração pronta, ative o agente para solicitar a primeira coleta operacional.',
          },
        },
      },
      finalCta: {
        title: 'Pronto para enxergar sua operação com mais clareza?',
        description:
          'Comece pelo conector disponível hoje e prepare a base para ampliar suas integrações amanhã. Crie sua conta gratuitamente e explore o ATUA durante o período de avaliação.',
      },
      footer: {
        tagline: 'ATUA — Plataforma operacional para empresas de serviços técnicos.',
        linksLabel: 'Links do rodapé',
        copyright:
          '© {{year}} Assistência Técnica Unificada Ltda. Todos os direitos reservados.',
        earlyStageNotice:
          'O ATUA está em fase inicial. Algumas funcionalidades podem estar em desenvolvimento ou sujeitas a alteração.',
      },
    },
  },
  'en-US': {
    translation: {
      metadata: {
        title: 'ATUA — Operations platform for technical service companies',
        description:
          'ATUA unifies data and operational visibility for technical service companies in a platform for multiple providers.',
      },
      common: {
        logoAlt: 'ATUA',
        actions: {
          signUp: 'Start for free',
          signIn: 'Sign in',
          seeHowItWorks: 'See how it works',
          createAccount: 'Create account',
          createAccountFree: 'Create a free account',
          existingAccountSignIn: 'I already have an account — Sign in',
        },
      },
      languageSelector: {
        label: 'Select language',
        optionLabel: 'Change language to {{language}}',
        languages: {
          'pt-BR': 'Português (Brasil)',
          'en-US': 'English (United States)',
          'es-AR': 'Español (Argentina)',
        },
      },
      hero: {
        eyebrow: 'Unified technical connectivity',
        headline:
          'Your technical operations, unified in a platform for multiple providers.',
        subheadline:
          'ATUA organizes data, tracking, and operational visibility for technical service companies—starting with the iService connector and ready to evolve with new providers.',
        badgesLabel: 'Platform features',
        badges: {
          unifiedPlatform: 'One platform',
          connectedData: 'Connected data',
          multipleProviders: 'Multiple providers',
        },
        diagram: {
          title: 'Operations center',
          inputLabel: 'Data input',
          inputValue: 'Multiple providers',
          layerLabel: 'ATUA layer',
          layerValue: 'Unified data',
          outputLabel: 'Output for your team',
          outputValue: 'Operational overview',
        },
      },
      platform: {
        eyebrow: 'What changes in practice',
        title: 'One platform for your entire operation',
        description:
          'ATUA is the operational layer for technical service companies that need to transform scattered sources into organized data, clear insights, and shared context.',
        cards: {
          organizedData: {
            title: 'Organized data',
            description:
              'Turn operational readings into a clearer internal data foundation.',
          },
          lessFriction: {
            title: 'Less friction',
            description:
              'Reduce switching between systems, spreadsheets, and scattered queries.',
          },
          multipleProviders: {
            title: 'Built for multiple providers',
            description:
              'Start with the initial connector without limiting the platform’s evolution.',
          },
          continuousGrowth: {
            title: 'Continuous growth',
            description:
              'Prepare your operation for new providers and gradual automation.',
          },
        },
      },
      audience: {
        eyebrow: 'Connectors and providers',
        title: 'Start with the providers your operation already uses',
        description:
          'ATUA is designed for multiple sources. iService is the initial connector, but it is not the platform’s limit.',
        connectorLabel: 'Connector',
        providers: {
          iservice: {
            name: 'iService',
            status: 'Initial connector',
          },
          new: {
            name: 'New providers',
            status: 'Planned',
          },
          future: {
            name: 'Future providers',
            status: 'Expandable foundation',
          },
        },
      },
      trust: {
        eyebrow: 'Step-by-step evolution',
        title: 'Reliable data first. Then automation with control.',
        description:
          'At this stage, ATUA collects and organizes information without performing operational actions in the provider. This foundation delivers visibility now and prepares the way to automate future steps with clear rules, authorization, and traceability.',
        cards: {
          reading: {
            title: 'Read-only stage',
            description:
              'The current stage prioritizes collecting and organizing provider data.',
          },
          traceability: {
            title: 'Traceability',
            description:
              'Commands, attempts, and results are recorded for monitoring.',
          },
          gradualAutomation: {
            title: 'Gradual automation',
            description:
              'New actions are introduced one at a time when sufficient operational control is in place.',
          },
        },
      },
      steps: {
        title: 'Get started in three steps',
        items: {
          account: {
            title: 'Create your account',
            description:
              'Sign up with your email and password. Confirm your email and your trial starts automatically—no credit card required.',
          },
          sources: {
            title: 'Connect your sources',
            description:
              'In your account, configure the initial connector available for your operation.',
          },
          collector: {
            title: 'Activate the Collector Agent',
            description:
              'Once configuration is complete, activate the agent to request the first operational data collection.',
          },
        },
      },
      finalCta: {
        title: 'Ready to see your operation more clearly?',
        description:
          'Start with the connector available today and build the foundation to expand your integrations tomorrow. Create your free account and explore ATUA during the trial period.',
      },
      footer: {
        tagline: 'ATUA — Operations platform for technical service companies.',
        linksLabel: 'Footer links',
        copyright:
          '© {{year}} Assistência Técnica Unificada Ltda. All rights reserved.',
        earlyStageNotice:
          'ATUA is in an early stage. Some features may be under development or subject to change.',
      },
    },
  },
  'es-AR': {
    translation: {
      metadata: {
        title: 'ATUA — Plataforma operativa para empresas de servicios técnicos',
        description:
          'ATUA unifica datos y visibilidad operativa para empresas de servicios técnicos en una plataforma para múltiples proveedores.',
      },
      common: {
        logoAlt: 'ATUA',
        actions: {
          signUp: 'Comenzar gratis',
          signIn: 'Ingresar',
          seeHowItWorks: 'Ver cómo funciona',
          createAccount: 'Crear cuenta',
          createAccountFree: 'Crear cuenta gratis',
          existingAccountSignIn: 'Ya tengo una cuenta — Ingresar',
        },
      },
      languageSelector: {
        label: 'Seleccionar idioma',
        optionLabel: 'Cambiar idioma a {{language}}',
        languages: {
          'pt-BR': 'Português (Brasil)',
          'en-US': 'English (United States)',
          'es-AR': 'Español (Argentina)',
        },
      },
      hero: {
        eyebrow: 'Conectividad técnica unificada',
        headline:
          'Tu operación técnica, unificada en una plataforma para múltiples proveedores.',
        subheadline:
          'ATUA organiza datos, seguimiento y visibilidad operativa para empresas de servicios técnicos, comenzando con el conector iService y preparada para evolucionar con nuevos proveedores.',
        badgesLabel: 'Características de la plataforma',
        badges: {
          unifiedPlatform: 'Una sola plataforma',
          connectedData: 'Datos conectados',
          multipleProviders: 'Múltiples proveedores',
        },
        diagram: {
          title: 'Centro operativo',
          inputLabel: 'Entrada de datos',
          inputValue: 'Múltiples proveedores',
          layerLabel: 'Capa ATUA',
          layerValue: 'Datos unificados',
          outputLabel: 'Salida para tu equipo',
          outputValue: 'Visión operativa',
        },
      },
      platform: {
        eyebrow: 'Qué cambia en la práctica',
        title: 'Una plataforma para toda la operación',
        description:
          'ATUA es la capa operativa para empresas de servicios técnicos que necesitan transformar fuentes dispersas en datos organizados, una lectura clara y un contexto compartido.',
        cards: {
          organizedData: {
            title: 'Datos organizados',
            description:
              'Transformá las lecturas operativas en una base interna más clara.',
          },
          lessFriction: {
            title: 'Menos fricción',
            description:
              'Reducí el cambio entre sistemas, planillas y consultas dispersas.',
          },
          multipleProviders: {
            title: 'Base para múltiples proveedores',
            description:
              'Comenzá con el conector inicial sin limitar la evolución de la plataforma.',
          },
          continuousGrowth: {
            title: 'Crecimiento continuo',
            description:
              'Prepará la operación para nuevos proveedores y automatizaciones graduales.',
          },
        },
      },
      audience: {
        eyebrow: 'Conectores y proveedores',
        title: 'Comenzá con los proveedores que tu operación ya usa',
        description:
          'ATUA nace abierta a múltiples fuentes. iService es el conector inicial, pero no es el límite de la plataforma.',
        connectorLabel: 'Conector',
        providers: {
          iservice: {
            name: 'iService',
            status: 'Conector inicial',
          },
          new: {
            name: 'Nuevos proveedores',
            status: 'Planificado',
          },
          future: {
            name: 'Proveedores futuros',
            status: 'Base expansible',
          },
        },
      },
      trust: {
        eyebrow: 'Evolución por etapas',
        title: 'Primero, lectura confiable. Después, automatización con control.',
        description:
          'En esta etapa, ATUA recopila y organiza información sin ejecutar acciones operativas en el proveedor. Esta base brinda visibilidad ahora y prepara el camino para automatizar pasos futuros con reglas claras, autorización y trazabilidad.',
        cards: {
          reading: {
            title: 'Etapa de lectura',
            description:
              'La etapa actual prioriza la recopilación y organización de los datos del proveedor.',
          },
          traceability: {
            title: 'Trazabilidad',
            description:
              'Los comandos, intentos y resultados quedan registrados para su seguimiento.',
          },
          gradualAutomation: {
            title: 'Automatización gradual',
            description:
              'Las nuevas acciones se incorporan de a una cuando existe suficiente control operativo.',
          },
        },
      },
      steps: {
        title: 'Comenzá en tres pasos',
        items: {
          account: {
            title: 'Creá tu cuenta',
            description:
              'Registrate con tu correo electrónico y contraseña. Confirmá tu correo y tu período de prueba comenzará automáticamente, sin tarjeta de crédito.',
          },
          sources: {
            title: 'Conectá tus fuentes',
            description:
              'En el área de tu cuenta, configurá el conector inicial disponible para tu operación.',
          },
          collector: {
            title: 'Activá el Agente Recolector',
            description:
              'Con la configuración lista, activá el agente para solicitar la primera recopilación operativa.',
          },
        },
      },
      finalCta: {
        title: '¿Todo listo para ver tu operación con más claridad?',
        description:
          'Comenzá con el conector disponible hoy y prepará la base para ampliar tus integraciones mañana. Creá tu cuenta gratis y explorá ATUA durante el período de prueba.',
      },
      footer: {
        tagline: 'ATUA — Plataforma operativa para empresas de servicios técnicos.',
        linksLabel: 'Enlaces del pie de página',
        copyright:
          '© {{year}} Assistência Técnica Unificada Ltda. Todos los derechos reservados.',
        earlyStageNotice:
          'ATUA está en una etapa inicial. Algunas funcionalidades pueden estar en desarrollo o sujetas a cambios.',
      },
    },
  },
} as const

export function normalizeLocale(language?: string | null): SupportedLocale | undefined {
  if (!language) {
    return undefined
  }

  const normalizedLanguage = language.trim().replace('_', '-').toLowerCase()
  const exactLocale = SUPPORTED_LOCALES.find(
    (locale) => locale.toLowerCase() === normalizedLanguage,
  )

  if (exactLocale) {
    return exactLocale
  }

  if (normalizedLanguage.startsWith('pt-') || normalizedLanguage === 'pt') {
    return 'pt-BR'
  }

  if (normalizedLanguage.startsWith('en-') || normalizedLanguage === 'en') {
    return 'en-US'
  }

  if (normalizedLanguage.startsWith('es-') || normalizedLanguage === 'es') {
    return 'es-AR'
  }

  return undefined
}

export function resolveInitialLocale(
  storedLocale?: string | null,
  browserLanguage?: string | null,
): SupportedLocale {
  return normalizeLocale(storedLocale) ?? normalizeLocale(browserLanguage) ?? DEFAULT_LOCALE
}

function readStorage(key: string): string | null {
  try {
    return globalThis.localStorage?.getItem(key) ?? null
  } catch {
    return null
  }
}

function writeStorage(key: string, value: string): void {
  try {
    globalThis.localStorage?.setItem(key, value)
  } catch {
    // The language remains available for the current session when storage is blocked.
  }
}

export function hasExplicitLocaleChoice(): boolean {
  return (
    normalizeLocale(readStorage(LOCALE_STORAGE_KEY)) !== undefined &&
    readStorage(EXPLICIT_LOCALE_STORAGE_KEY) === 'true'
  )
}

export function persistLocale(locale: SupportedLocale, explicit: boolean): void {
  writeStorage(LOCALE_STORAGE_KEY, locale)
  writeStorage(EXPLICIT_LOCALE_STORAGE_KEY, String(explicit))
}

const storedLocale = readStorage(LOCALE_STORAGE_KEY)
const initialLocale = resolveInitialLocale(
  storedLocale,
  typeof navigator === 'undefined' ? undefined : navigator.language,
)
const initialLocaleWasExplicit =
  normalizeLocale(storedLocale) !== undefined &&
  readStorage(EXPLICIT_LOCALE_STORAGE_KEY) === 'true'

persistLocale(initialLocale, initialLocaleWasExplicit)

if (typeof document !== 'undefined') {
  document.documentElement.lang = initialLocale
}

void i18n.use(initReactI18next).init({
  fallbackLng: DEFAULT_LOCALE,
  interpolation: { escapeValue: false },
  lng: initialLocale,
  load: 'currentOnly',
  resources,
  supportedLngs: SUPPORTED_LOCALES,
})

export default i18n
