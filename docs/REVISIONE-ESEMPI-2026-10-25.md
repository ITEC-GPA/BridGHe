# Revisione degli esempi del 25 ottobre 2026

La Release applica al percorso FEM → verificatore H l’ipotesi **N–Mx–Vy** concordata: torsione T, flessione sull’asse debole M2 e taglio trasversale V1 sono trascurati nelle verifiche di sezione e dei dettagli. Le sei componenti rimangono nei modelli, nell’uscita Tension Forces e nei grafici delle sollecitazioni. Gli effetti esclusi e le loro interazioni non sono verificati.

L’ipotesi è visibile sui componenti, conservata in `BridgeStressResult.Assumptions`, propagata da Bridge Detail Check ed esportata nel riepilogo obbligatorio del report e nel suo archivio numerico. Il percorso LinearHistory conserva le proprie limitazioni: risposta N–Mx, senza verifiche di taglio, pioli e irrigiditori. Il componente nativo gPC per sezioni/cassoni non perde le capacità torsionali che già esponeva: la proiezione riguarda l’adattatore dei risultati FEM al checker H.

## Esempio 50

[Aprire il workflow e le istruzioni](../example/50-ponte-quattro-modelli/README.md).

- Quattro modelli e quattro fasi numerate: acciaio, n∞, n0, n ritiro.
- Sei azioni meccaniche di prova nei primi tre modelli; un’unica azione di ritiro nel quarto. Lo stesso εcs calcolato alimenta deformazioni equivalenti FEM e autotensione primaria.
- Le azioni riferiscono tutto il modello, mantenendo le reazioni globali. La selezione delle travi principali avviene dopo il Build.
- Bridge Segment Composite associa le sezioni miste per concio alle aste FEM con risultati; non presuppone una sola asta per concio.
- Il verificatore copre tutte le 108 aste longitudinali. Tre rami raccolgono sezioni/pioli, irrigiditori ai giunti e irrigiditori agli appoggi con le reazioni delle stesse fasi.
- Bridge Girder Results conserva gli alberi e seleziona una trave per i sei grafici; il report riceve tutte le travi e i dettagli.
- Il test di collegamento produce 1080 risultati in una copia temporanea, usando dati sintetici anche con T, M2 e V1 non nulli. La copia viene scartata: il GHX consegnato non contiene risultati FEM inventati.

I carichi sono incrementi distinti; le combinazioni DEMO non applicano coefficienti normativi. La lunghezza di pannello degli irrigiditori è un’ipotesi esplicita di 12 m, da verificare sulle posizioni effettive. Le analisi Straus e il report numerico reale del 50 restano da eseguire.

## Controlli eseguiti

| Ambito | Risultato effettivo |
|---|---|
| Release | Compilazione senza errori o avvisi; distribuzione in `bin/Release` |
| Suite offline generale | 232 controlli smoke e 73 gruppi di regressione; tre ulteriori gruppi per grafici, preview e componente report |
| Contratti GH | 52 definizioni correnti riaperte; 5232 oggetti e 5307 collegamenti conservati |
| Esempi portabili 23–45 | 23 documenti risolti; 299 oggetti e 255 collegamenti |
| Esempio 11 | Replay dai risultati archiviati: 166 misure, 240 risultati di sezione |
| Esempi 12–18 | Replay dei sette ponti: 224 misure, incluse le risposte N–Mx–Vy dei curvi e i dettagli |
| Esempio 19 REV003 | Replay dei quattro `.bgmodel` archiviati: 378 risultati di sezione, nessun componente rosso |
| Esempio 99 REV002 | Riapertura e ricalcolo del workflow salvato: 384 risultati di sezione, un ramo di verifica |
| Esempio 99 REV003 | Riapertura e ricalcolo del workflow salvato: 648 risultati di sezione, due rami di verifica |
| Esempio 99 REV004 | Ritiro Excel/FEM/primario allineato; quattro cache obsolete invalidate. Riapertura senza errori, riferimenti Excel validi; analisi native in attesa |
| Esempio 50 | Geometria, proprietà, massa, carichi, fasi, ritiro, riapertura, verifiche e grafici con fixture temporanea superati |
| Guida Word | Campo di verifica e tabella diagnostica aggiornati; 32 pagine renderizzate e impaginazione controllata |

I replay usano risultati Straus preesistenti: **non sono nuove analisi native**. I benchmark 01–10, le cinque prove mobili del 21 e le prove native delle fasi del 22 conservano i rispettivi report storici; non sono stati rieseguiti sul solutore durante questa revisione. Le regressioni offline di carichi mobili, fasi, ritiro e interpolazione sono incluse nella suite generale.

## Allineamento del 99 REV004

È stato scelto il valore calcolato dagli input attuali: **εcs = −0,0003171439341670765**, circa **−317 µε**. Lo stesso valore alimenta azione primaria e generatore delle deformazioni equivalenti FEM. `Combinazioni_REV004.xlsx`, `Actions!I4`, è stato allineato; tutti i coefficienti di Combinations e gli altri dati/formati del workbook sono conservati. Il precedente riferimento era circa −199 µε.

L’ispezione ha rilevato anche geometrie obsolete nelle quattro cache: 37/76/76/76 aste contro le 73/112/112/112 attuali. Anche prestrain e gradiente del ritiro salvati differivano dagli input attuali. Sono quindi state invalidate **tutte e quattro** le cache, mantenendo i modelli attuali senza risultati. Non sono stati riscalati o inventati risultati numerici.

Le note nel GHX indicano ora la sequenza: quattro analisi Straus → Read Excel → Build → Check → Generate report. Il file si riapre senza componenti rossi e il template viene accettato; l’audit segnala `pending-native`, non una verifica numerica superata. Gli ST7/LSA e il report già presenti sono riferimenti storici e devono essere rigenerati. Il dettaglio è in [alignment-validation.json](../example/99-Ponte%20completo%20GPC/REV004/alignment-validation.json).

La pubblicazione ha sostituito soltanto le quattro cache e due note: geometria, input, collegamenti e GUID personali sono conservati. La copia precedente è in `artifacts/align99/flusso-before.ghx`; il workbook precedente è in `artifacts/align99/Combinazioni-before.xlsx`. Il 99 REV003, il 49 originale e la copia personale del 49 sono rimasti invariati.

## Differenze mantenute consapevolmente

- Il 11 usa due deformazioni primarie imposte; non rappresenta la stessa prova di primario + secondario del 50.
- Nei 12–18 n ritiro coincide con n∞, coerentemente con le analisi archiviate. Non viene sostituito automaticamente con il valore dimostrativo 12 del 50.
- Nei 16–19 il ramo di verifica seleziona la spina centrale. Non si dichiara la verifica di tutte le travi laterali; il 50 introduce invece la copertura di tutte le travi principali.
- I report storici che attestavano il rifiuto della torsione restano dati storici. Le note nei GHX correnti 11–18 e le istruzioni ora descrivono la proiezione piana; geometria, input e risultati archiviati non sono stati riscritti.
- Fessurazione della soletta, stabilità globale, fatica, traversi, soletta locale, dispositivi di appoggio e combinazioni di progetto richiedono i propri controlli. I risultati di questo audit non attestano la verifica completa di un ponte.

## Correzioni isolate

1. I replay 11–18 ora scrivono in `artifacts/example-replay`, evitando di sovrascrivere gli esempi distribuiti.
2. Bridge Plot Preview tratta una stringa SVG vuota come attesa dei risultati, svuota l’immagine precedente e conserva gli indici della lista. Un SVG non vuoto ma malformato resta un errore.
3. L’audit dei contratti riconosce correttamente i metodi C# con corpo espressione: non scambia più una modifica a SolveInstance per una modifica alle porte. Tre test Python verificano il parser.
4. La trasformazione dei default numerici del 50 in slider conserva la precisione originale: una tolleranza di 10⁻⁶ non diventa zero per arrotondamento a tre decimali.

5. Bridge Action e Bridge Shrinkage Action ora producono definizioni anche su modelli ancora privi di risultati. Le Value List includono casi e combinazioni definiti. Ciò permette di preparare il template Excel; il Build continua a rifiutare risultati FEM mancanti. La regressione esercita entrambe le azioni, catalogo e rifiuto del Build.

## Riprodurre i controlli senza Straus

```powershell
./tools/build.ps1 -Smoke
py tools/test_audit_contracts.py
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --grillage-gh-replay
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-gh-replay all
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --workflow-archived-replay
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --saved-bridge-audit
```

L’ultimo comando restituisce codice 2 per il 99 REV004 predisposto ma ancora da analizzare; codice 1 indica errori e 0 indica tutti i workflow calcolati e verificati. Non modifica i GHX, non genera report e non chiama Straus. I log e i report di questa esecuzione si trovano in `artifacts/review-smoke-final.log`, `artifacts/replay-*-final.log`, `artifacts/saved-bridge-workflow-audit.json` e `artifacts/phase-example-final.log`.
