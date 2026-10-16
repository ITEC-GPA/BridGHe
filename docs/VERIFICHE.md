# Verifiche della versione 0.1.0

## Risultati effettivamente ottenuti

Ambiente: Windows x64, Rhino 8 avviato senza finestra mediante Rhino.Inside, runtime .NET 8, compilazione con SDK 9.0.319. Data: 16 ottobre 2026.

- Build Release: **0 errori, 0 avvisi**.
- Audit delle porte: **103 componenti/parametri mantenuti**, inclusi **16 MeCheck2**, nessuna differenza non ammessa.
- **137 GUID distinti** contando plugin e oggetti Grasshopper; nessuna collisione con i GUID della base.
- Smoke test isolato dopo le correzioni: **177 controlli superati e 19 gruppi di regressione superati**, inclusa l'istanziazione di tutti i **136 componenti/parametri registrabili** nel runtime Rhino 8.
- Icone: tutte le 136 risorse presenti, distinte e associate al proprio oggetto a 24×24 px; logo plugin a 24 px; registrazione delle due categorie con icone a 16 px verificata nella sessione precedente; il controllo corrente sul profilo completo è rimasto bloccato durante il caricamento dei plugin installati. Confronto dei pixel opachi e alpha esatto, tolleranza di due livelli su 255 sui canali premoltiplicati dei bordi semitrasparenti per gli arrotondamenti GDI+.
- Prove analitiche: area/inerzia del cerchio, sezione omogeneizzata di solo acciaio, tensione N/A, flessione con segno dei lembi, taglio VQ/(It).
- Fasi: azioni istantanee e a lungo termine distinte, accumulo delle deformazioni imposte, errore esplicito per risultati mancanti alla stazione.
- Persistenza: archivio del modello con nodi, beam, attributi e risultati; identità condivisa dei casi di carico; archivio Grasshopper di sezioni, proprietà e proprietà composte; dati e copia profonda dei carichi mobili.
- Input non supportati: rifiuto di torsione nel percorso MeCheck2 adottato, offset verticale non reinterpretato, mobilità dei carichi non valida.

Il log corrente è `artifacts/bugfix-validation.log`; audit e inventario sono in `artifacts/contract-audit.json` e `artifacts/component-runtime.json`. Gli artefatti sono esclusi da Git. La suite isolata termina con `PASS TOTAL 177`, `REGRESSION TOTAL 19` e codice zero.

Il precedente totale 179 includeva due controlli di registrazione delle categorie. Ora sono opzionali perché `Grasshopper.Instances.ComponentServer` carica anche i plugin del profilo: il tentativo corrente è rimasto bloccato dopo il controllo delle icone del plugin ed è stato interrotto. Il precedente log riportava inoltre errori di plugin Tekla estranei a BridGHe. Per eseguire quei due controlli d'integrazione: `dotnet run --project tests/BridGHe.Smoke -c Release -- --installed-plugins`. Non sono conteggiati come superati nella validazione corrente.

## Regressioni della revisione correttiva

Sono coperti i difetti R1–R9 della revisione e i problemi aggiuntivi emersi durante le correzioni. Ogni fix ha un commit distinto; elenco in [MIGRAZIONE](MIGRAZIONE.md#revisione-correttiva).

- Somma di forze e momenti nodali, identità dei casi omonimi, rifiuto delle definizioni incompatibili.
- Merge dei dati nodali, topologia delle mesh multifaccia, connettività FEM delle piastre e indipendenza degli input.
- GUID degli elementi attraverso output, duplicazione e archivio GH; copie indipendenti con riferimenti condivisi conservati all'interno della copia.
- Ripristino delle travi composte, sostituzione dei solidi obsoleti, validità di sezioni native e proprietà cutoff.
- Casi esclusivi delle fasi, selezione qualificata `Combinazione [Fase]`, conservazione degli indici di incremento e rifiuto dei casi incompatibili con il solutore.
- Indici dei risultati e dei nodi confrontati con somme analitiche e ricerca esaustiva; coefficienti negativi, conversione dei momenti, stazioni normalizzate, soglie di tolleranza, risultati mancanti/duplicati.
- Aggiornamento degli attributi dopo archivio, sostituzione/svuotamento dei punti delle piastre, travi incomplete e invalidazione della cache delle bounding box.

La separazione fra errori bloccanti e avvisi nell'esportatore e le descrizioni di validità dei Goo sono state controllate sul codice e compilate. Il comportamento delle chiamate native nei percorsi di errore non è attestato senza una prova API.

### Microbenchmark MeCheck2

Un elemento, 8 casi, 10 combinazioni, mediana di tre esecuzioni dopo riscaldamento. L'indice viene ricostruito una volta per esecuzione e il suo costo è incluso. Checksum analitico delle azioni verificato in ogni esecuzione.

| Stazioni | Revisione iniziale | Suite corrente | Allocazioni correnti |
|---|---:|---:|---:|
| 100 | 64,6 ms | 7,6 ms | 10,3 MiB |
| 400 | 482,3 ms | 19,9 ms | 41,2 MiB |
| 800 | 1059,5 ms | 40,1 ms | 82,4 MiB |

La regressione mirata precedente aveva misurato 37,6 ms a 800 stazioni. I tempi dipendono dalla sessione e non rappresentano il tempo di verifica di un ponte completo. L'allocazione resta rilevante per gli oggetti delle azioni gPC. La ricerca nodale usa un RTree riutilizzato e conserva la selezione per ordine delle coordinate e la soglia stretta `< tolerance`; è verificata contro ricerca esaustiva, senza attribuirle un'accelerazione numerica non misurata.

Restano da profilare copie su modelli completi, risultati e anteprime. Le copie profonde usano l'archivio già presente per garantire l'isolamento; non viene dichiarata una riduzione dei loro costi. L'esecuzione del solutore resta sincrona e serializzata dal gate API.

## Prova nativa non eseguita

La DLL R3 è stata caricata, ma `St7Init` ha restituito:

```text
Straus -8: The API is running in STANDALONE mode but cannot find the hardware lock.
```

Il test stampa `NATIVE SKIPPED`; questo **non è un test nativo superato**. Non sono quindi attestati esportazione completa, proprietà native delle nuove sezioni, generazione BXS, distribuzione dei carichi mobili, solutori LSA/NLA/staged e associazione dei risultati dopo una reale soluzione R3.

Per ripetere:

```powershell
.\tools\build.ps1 -Native
```

Con licenza disponibile, il test tenta il calcolo di tutte le 21 sezioni parametriche e un benchmark di mensola: L=3000 mm, E=210000 N/mm², A=60000 mm², I11=450000000 mm⁴, carico terminale globale Z=-1000 N, sei vincoli all'origine, peso proprio escluso dal caso selezionato e aree di taglio nulle. Confronta lo spostamento con `-F L³/(3 E I) = -0.095238095 mm` e il modulo del momento alla radice con `F L = 3000000 Nmm`; tolleranza relativa 1e-5. **Questo benchmark è predisposto nel codice, ma non eseguito nella sessione senza licenza.**

## Limiti espliciti della trasposizione

| Ambito | Stato |
|---|---|
| MeCheck2 | Tutti i componenti e le porte ricreati. Il percorso adottato del motore gPC copre una singola H diritta con soletta rettangolare e fino a due file di barre a diametro uniforme per fila, sotto N–Mx–Vy. Flessione deviata, torsione, taglio trasversale e sezioni ruotate/asimmetriche sono rifiutati. |
| Altri checker della base | I componenti esterni a MeCheck2 sono stati trasposti e compilati; non sono stati eseguiti benchmark numerici completi per ciascuno. Le convenzioni storiche di questi percorsi richiedono una validazione dedicata prima dell’uso progettuale. |
| Metodo di verifica | La trasposizione usa il motore gPC disponibile, con opzioni Eurocode 4 / SLE rara per la risposta tensionale. Non attesta tutte le verifiche NTC/Eurocodici di un ponte, né equivalenza numerica completa al vecchio checker. |
| Ritiro calcolato dal tempo | `epsilonCS=NaN`; resta utilizzabile il ritiro **imposto** nei componenti di fase/self tension. |
| TauB | `NaN` e warning finché non è definita una corrispondenza verificata. |
| Effective Epsilon | `true` non supportato dal nuovo adattatore; usare `false` per il metodo corrente a larghezze efficaci. |
| Vertical Offset della sezione composta | Supportato solo zero: il parametro della nuova libreria ha significato differente. |
| Sezioni native nuove | Calcolo/export da validare con API; niente anteprima solida estrusa; moduli resistenti non letti = `NaN`. |
| Mobile | Casi statici discreti; niente inviluppo da linee d'influenza. Target Entity Set/Brick non esportabili. Analisi termica da eseguire in Straus. |
| Link Property legacy nei supporti | Porta mantenuta, utilizzo esplicitamente rifiutato; disponibili le rigidezze dei supporti gestite da R3. |
| Cataloghi esterni | Selezione dai vecchi database rimossa; usare componenti materiali/sezioni. |
| Dati legacy e bake | Nuovi GUID e archivio BGHA1; nessuna importazione automatica dei vecchi documenti. Bake di geometria nativa senza metadati strutturali. |

## Validazione successiva, concreta

1. Eseguire il benchmark nativo predisposto e confrontare anche verso delle reazioni, SF1/SF2, BM1/BM2 e orientamento di beam orizzontali, verticali e ruotati.
2. Confrontare standard/BGL/BXS/librerie con il pannello proprietà Straus, comprese sezioni non simmetriche e unità m/mm.
3. Verificare un singolo asse mobile su trave appoggiata confrontando reazioni e momento con la soluzione statica per ogni posizione; poi multicorsia, carico distribuito, curva e centrifuga. Raffinare le divisioni per stabilizzare i massimi.
4. Verificare un caso composto per fase, con modello elastico indipendente, confermando baricentri, rapporti modulari, segni, ritiro e riduzioni di classe 4. Controllare separatamente i risultati con soletta esclusa.
5. Eseguire almeno un caso NLA e uno staged, confrontando nomi/indici dei casi salvati e risultati riassociati agli elementi.

Questi passaggi non sono sostituiti dalla sola compilazione. I controlli software già eseguiti rendono la trasposizione riproducibile e ispezionabile; la validazione del verificatore strutturale completo rimane distinta.
