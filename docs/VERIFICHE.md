# Verifiche della versione 0.1.0

## Risultati effettivamente ottenuti

Ambiente: Windows x64, Rhino 8 avviato senza finestra mediante Rhino.Inside, runtime .NET 8, compilazione con SDK 9.0.319. Data: 16 ottobre 2026.

- Build Release: **0 errori, 0 avvisi**.
- Audit delle porte: **103 componenti/parametri mantenuti**, inclusi **16 MeCheck2**, nessuna differenza non ammessa.
- **137 GUID distinti** contando plugin e oggetti Grasshopper; nessuna collisione con i GUID della base.
- Smoke test: **179 controlli superati**, inclusa l'istanziazione di tutti i **136 componenti/parametri registrabili** nel runtime Rhino 8.
- Icone: tutte le 136 risorse presenti, distinte e associate al proprio oggetto a 24×24 px; logo plugin a 24 px; registrazione delle due categorie riuscita con icone a 16 px. Confronto dei pixel opachi e alpha esatto, tolleranza di due livelli su 255 sui canali premoltiplicati dei bordi semitrasparenti per gli arrotondamenti GDI+.
- Prove analitiche: area/inerzia del cerchio, sezione omogeneizzata di solo acciaio, tensione N/A, flessione con segno dei lembi, taglio VQ/(It).
- Fasi: azioni istantanee e a lungo termine distinte, accumulo delle deformazioni imposte, errore esplicito per risultati mancanti alla stazione.
- Persistenza: archivio del modello con nodi, beam, attributi e risultati; identità condivisa dei casi di carico; archivio Grasshopper di sezioni, proprietà e proprietà composte; dati e copia profonda dei carichi mobili.
- Input non supportati: rifiuto di torsione nel percorso MeCheck2 adottato, offset verticale non reinterpretato, mobilità dei carichi non valida.

Log e inventari della sessione sono in `artifacts/final-validation.log`, `artifacts/icon-validation.log`, `artifacts/contract-audit.json`, `artifacts/component-runtime.json`. Gli artefatti sono esclusi dal controllo versione; il rapporto presente è il riepilogo persistente. Durante il test delle categorie, Grasshopper ha caricato anche plugin installati nel profilo: il log riporta eccezioni di connessione Tekla relative a `System.Runtime.Remoting`; i controlli BridGHe sono terminati con `PASS TOTAL 179` e codice di uscita zero.

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
