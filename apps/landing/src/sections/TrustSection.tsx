export function TrustSection() {
  return (
    <section aria-labelledby="trust-heading" className="mx-auto max-w-3xl px-4 py-16">
      <div className="rounded-2xl border-2 border-primary bg-primary/5 p-8 text-center shadow-sm sm:p-12">
        <h2 id="trust-heading" className="text-2xl font-bold text-primary sm:text-3xl">
          O ATUA observa. Nunca interfere.
        </h2>
        <p className="mt-4 text-lg text-slate-700">
          O Agente Coletor do ATUA opera exclusivamente em modo somente leitura. Isso
          significa que o ATUA nunca escreve, altera, aceita ou reatribui ordens de
          serviço no seu iService. Seus dados no iService permanecem intactos,
          exatamente como a sua equipe os registrou. O ATUA coleta, organiza e
          apresenta — a operação é sempre sua.
        </p>
      </div>
    </section>
  )
}
