# 46 — Template Excel delle combinazioni

**Aggiornamento 14 novembre 2026 (BridGHe 0.4.0).** I sette modelli salvati nella 0.3.0 (uno per ognuna delle quattro azioni, uno per ciascun Bridge Combination Excel e uno per Build BridGHe Model) non si potevano più leggere nella 0.4.0: le due aste non avevano i nodi agli estremi (messaggio «Nessun nodo FEM entro tolleranza alle coordinate 0, 0, 0»). Sono stati rifatti con Build Model sugli stessi dati della 0.3.0, dati con i costruttori GPC: due aste parallele B0 e B1 lunghe 10 m, a 2 m di distanza, sulla proprietà «Longitudinale fase 0»; Build Model aggiunge i quattro nodi agli estremi. **Il modello dimostrativo è ora costruito senza il link rigido** della copia precedente. I modelli portano le bozze dei loro elementi (Decompose Model e Super Element Filter li accettano). I risultati salvati (caso G) sono gli stessi campioni di prima; le associazioni delle quattro azioni (G1, G2, Q, RITIRO) e i coefficienti delle combinazioni sono conservati: leggendo Combinations.xlsx, Build BridGHe Model dà le due combinazioni DEMO_01 e DEMO_02. Componenti, GUID e collegamenti non cambiano; il file passa da 1.053.683 a 367.508 byte. Template.xlsx e Combinations.xlsx non sono stati riscritti: un template creato oggi dal file nuovo è uguale a Template.xlsx, foglio per foglio. La copia precedente resta nella cronologia del repository (commit `9350aeb`).

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

Aprire [46-combinazioni-excel.ghx](46-combinazioni-excel.ghx). Il grafo contiene quattro azioni, tre fasi, due istanze di **Bridge Combination Excel** e **Build BridGHe Model**. I modelli contengono campioni sintetici di risultati su due aste (dati GPC della 0.4.0, senza link rigido): servono a provare lo scambio dati, non costituiscono un'analisi strutturale del ponte.

| Fase | Azione | Comportamento | Caso sorgente |
|---|---|---|---|
| 10 Getto su acciaio | G1 | Steel | G nel modello Steel |
| 20 Permanenti portati | G2 | LongTerm | G nel modello Composite long term |
| 30 Esercizio e ritiro | Q | ShortTerm | G nel modello Composite short term |
| 30 Esercizio e ritiro | RITIRO | Shrinkage, Δε = −0.0002 | G nel modello Shrinkage secondary |

Le quattro colonne sono **G1, G2, Q, RITIRO**: nessun prefisso, anche se i casi originali si chiamano tutti G. Il coefficiente di RITIRO moltiplica insieme sollecitazioni secondarie FEM e strain primario.

## Provare la lettura

1. Sul componente **2 Leggi Excel** è già impostato `Mode = 1` e il file relativo `Combinations.xlsx`.
2. Premere **Run** sul lettore. `Table` è già collegata all'input `Combinations` del Build.
3. Premere **Run** su Build BridGHe Model. Si ottengono due combinazioni: DEMO_01 e DEMO_02.
4. Modificare un coefficiente in [Combinations.xlsx](Combinations.xlsx), salvare, poi ripetere i due Run. I coefficienti dimostrativi non sono prescrizioni normative.

## Creare un proprio template

1. Sul componente **1 Crea template**, cambiare File in un nuovo nome, ad esempio `MieCombo.xlsx`, e premere Run.
2. Il file [Template.xlsx](Template.xlsx) già fornito mostra il risultato vuoto. Per rigenerarlo occorre autorizzarne esplicitamente la sostituzione con `Overwrite = True`, dopo aver chiuso Excel.
3. Compilare solo Combinations: nome univoco in colonna A, tutti i coefficienti nelle colonne successive. Indicare 0 dove l'azione non partecipa; lasciare interamente vuote le righe inutilizzate.
4. Il foglio Actions documenta le associazioni. Non modificarlo. I Beam IDs `[]` significano applicazione a tutte le aste.
5. Impostare lo stesso File nel lettore e premere Run, quindi Run sul Build.

I percorsi relativi sono risolti nella cartella del GH salvato. Aprire il GH non esegue operazioni Excel. Un errore di lettura svuota Table, evitando di lasciare coefficienti precedenti disponibili. Le formule usano il risultato salvato: ricalcolare e salvare in Excel prima della lettura. Il plugin non richiede Excel installato per creare o leggere file numerici.

## Test riproducibili

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --regression bridge-combination-excel
dotnet run --project tests/BridGHe.Smoke -c Release -- --regression bridge-combination-excel-component
# Rigenera questo esempio e i due workbook, sostituendo eventuali modifiche manuali:
dotnet run --project tests/BridGHe.Smoke -c Release -- --combination-excel-example
```

Il report [validation.json](validation.json) registra creazione, lettura, Build, riapertura GHX, percorsi relativi, Run esplicito e rimozione delle uscite obsolete. Nessuna nuova analisi Straus viene eseguita.
