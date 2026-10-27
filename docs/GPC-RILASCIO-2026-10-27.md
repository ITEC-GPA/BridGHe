# Rilascio ufficiale GPC — 27 ottobre 2026

Il push autorizzato di **Checker/master** è completato: GitHub conferma `4a1b0090fe4de64de54e28cb9d380c441e79660f`. Model resta a `2acc60ef`, Utilities a `e57b440b` e Geometry a `6734eee1`; tutti i commit del set sono pubblicati su `origin/master`.

Il set è stato prodotto nuovamente con `Checker/build/Release-Set.ps1`, senza `-Candidate`, da checkout puliti e usando il precedente manifest ufficiale come riferimento per il controllo delle versioni. Il [manifest distribuito](../lib/GPC/release-set.json) contiene **`candidate=false`** e `onOriginMaster=true` per tutti i repository.

## Binari e versioni

**Tutte le 14 DLL hanno SHA-256 identico al candidato collaudato il 26 ottobre.** Non è necessario incrementare altre versioni: Concrete resta 0.25.0.0, Steel 2.118.0.2, CompositeBridge 1.19.0.5 e Model 5.21.0.1. L'elenco completo è nel manifest e nel [resoconto d'integrazione](GPC-INTEGRAZIONE-2026-10-26.md).

I file GPC dei plugin non cambiano contenuto; vengono aggiornati i manifest di provenienza e ricompilati i plugin con la procedura ordinaria, senza consentire candidati. Le versioni restano BridGHe **0.2.0** e ReBear **0.2.2**.

## Controlli eseguiti il 27 ottobre

| Controllo | Esito |
|---|---|
| Suite Model | 1.493 superati, zero fallimenti; tre prove con fixture native facoltative non eseguite |
| Suite Checker | 9.046 superati, zero fallimenti; inclusi Concrete/Steel net472 e Design net8.0/net472 |
| Compatibilità API | Nessuna rimozione inattesa |
| Confronto del set con il candidato | 14/14 DLL identiche per SHA-256 e versione |
| Compilazione dei plugin | Release riuscita senza opzioni per candidati, zero errori e zero avvisi MSBuild |
| Controlli principali BridGHe | 269 superati, senza nuove analisi native Straus |
| Componenti BridGHe | Caricamento e archivi di 220 componenti e 8 parametri |
| Baseline numerica del ponte | 238/238 valori esattamente uguali; tolleranza relativa/assoluta `1e-12` |
| Esempio 50 REV003 | Riapertura e controllo del flusso superati, senza eseguire il solver |
| ReBear | 25 gruppi superati, zero fallimenti; caricamento di 61 componenti |
| Coerenza fra plugin | Cinque DLL comuni con SHA-256 identico |

Il [resoconto JSON](validation-gpc-release-2026-10-27.json) conserva manifest, hash distribuiti e prove di completamento. Gli 84 gruppi di regressione del 26 ottobre restano documentati nel resoconto precedente e riguardano gli stessi binari GPC; non sono conteggiati come rieseguiti oggi. Anche il collaudo nativo completo del REV003 conserva data e librerie della propria esecuzione, senza attribuirlo retroattivamente a questa pubblicazione.

## Cartelle pronte

- `BridGHe/bin/Release`: `BridGHe.Grasshopper.gha`, dipendenze e `gpc-release-set.json` ufficiale.
- `ConcreteSectionChecker/bin`: `ReBear.gha`, dipendenze e `release-set.json` ufficiale.
- `Checker/bin/Release`: set completo di 14 DLL con manifest e simboli disponibili.

Le cartelle `lib/GPC` di entrambi i plugin conservano lo stesso manifest ufficiale completo. La compilazione ordinaria controlla questo set; le opzioni per candidati rimangono disponibili esclusivamente per futuri collaudi espliciti.
