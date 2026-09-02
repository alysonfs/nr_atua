import { AudienceSection } from './sections/AudienceSection'
import { FinalCtaSection } from './sections/FinalCtaSection'
import { Footer } from './sections/Footer'
import { Hero } from './sections/Hero'
import { PlatformSection } from './sections/PlatformSection'
import { StepsSection } from './sections/StepsSection'
import { TrustSection } from './sections/TrustSection'

function App() {
  return (
    <>
      <Hero />
      <main>
        <PlatformSection />
        <AudienceSection />
        <TrustSection />
        <StepsSection />
        <FinalCtaSection />
      </main>
      <Footer />
    </>
  )
}

export default App
