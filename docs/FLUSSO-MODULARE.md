# Flusso modulare del ponte — 26 ottobre 2026

Il ponte si definisce con oggetti indipendenti, poi si discretizza con un solo componente. Le lunghezze della geometria sono in **mm**. I conci sono porzioni fisiche delle travi e mantengono la propria identità quando cambia la mesh FEM.

```text
Curva + progressive appoggi → Bridge Support Lines ──────────┐
Interassi/offset + quote → Bridge Girder Axes ────────────────┤
Larghezze + spessore + pendenza → Bridge Slab Layout ─────────┤
Progressive giunti → Bridge Joint Stations                   │
  + curva + Girder Axes → Bridge Fabrication Segments ───────┤
Passo/progressive traversi → Solid / Truss Diaphragms ───────┤
                                      Bridge Geometry Assembly
                                                  + Bridge Mesh Settings
                                                  ↓
                                         Bridge Grillage Mesh
                                                  ↓
                      proprietà + vincoli + connessioni → modello FEM
```

## Componenti e input

| Componente | Responsabilità e input principali |
|---|---|
| Bridge Support Lines | Distanze degli appoggi lungo la curva; eventuali angoli di obliquità in gradi. Input vuoto: estremi della curva. Gli appoggi intermedi vanno indicati esplicitamente. Non assegna vincoli FEM. |
| Bridge Support Stations | Helper facoltativo: ordina le progressive, accorpa duplicati entro tolleranza e può aggiungere gli estremi. |
| Bridge Girder Axes | Offset trasversali crescenti e quote degli assi; un valore di quota comune oppure uno per trave. Positivo a sinistra percorrendo il tracciato. |
| Bridge Regular Girders | Genera offset simmetrici da numero di travi e interasse; gli offset espliciti hanno precedenza. |
| Bridge Slab Layout | Larghezze sinistra/destra, spessore, pendenza trasversale percentuale costante o per progressive. Asse della soletta a metà spessore se non specificato. |
| Bridge Joint Stations | Progressive dei giunti e ID delle travi interessate. ID vuoti: tutte le travi. Si possono unire più oggetti. |
| Bridge Fabrication Segments | Curva, disposizione delle travi, giunti e lunghezza massima facoltativa. Espone numero, ID, estremi e lunghezze dei conci prima della mesh. |
| Bridge Solid Diaphragms | Traversi ad anima piena: passo oppure progressive esplicite e inclusione delle linee di appoggio. |
| Bridge Truss Diaphragms | Stessi controlli, più altezza, pannelli e schema della reticolare. |
| Bridge Geometry Assembly | Riunisce le definizioni; non produce elementi FEM. |
| Bridge Mesh Settings | Lunghezza massima degli elementi, tolleranza di corda, passo/progressive degli elementi trasversali della soletta. |
| Bridge Grillage Mesh | Unico motore di geometria e suddivisione. Produce carpenteria, soletta, nodi, punti di appoggio, connessioni e identificativi. Conserva la preview delle geometrie. |
| Bridge Family Property | Una proprietà FEM per famiglia selezionata per nome. “All steel families” esclude la soletta. |
| Bridge FEM Members | Geometria, proprietà delle famiglie ed eventuali regole di sezione per concio. Controllare sempre l'uscita Unassigned. |
| Bridge Rigid Links / Bridge Connection Beams | Connessioni cinematiche oppure aste con proprietà esplicita. Usare una sola rappresentazione per la stessa connessione. |
| Bridge Beam Offsets | Applica eccentricità locali alle beam; la quota geometrica dell'asse resta una scelta esplicita. |

`Bridge Span Stations`, `Bridge Alignment Curve`, `Bridge Deck Widths`, `Bridge Bracing Stations` e `Bridge Bearing Pattern` sono helper facoltativi. Gli importatori `Bridge Joint Tree` e `Bridge Crossfall Table` servono soprattutto a riutilizzare alberi già organizzati.

## Conci e sezioni da Excel

1. Collegare curva, Girder Axes e uno o più Joint Stations a **Bridge Fabrication Segments**.
2. In assenza di giunti espliciti per una trave, Maximum Segment Length genera suddivisioni uguali non superiori a quel valore; zero significa un solo concio. I giunti espliciti sostituiscono la suddivisione automatica della trave interessata.
3. Collegare Fabrication Plan a **Bridge Section Excel Template**, indicare il file e premere Run. Non sovrascrive un file esistente senza Overwrite.
4. Compilare il foglio Sezioni senza cambiare ID e progressive. `*` eredita la dimensione dalla proprietà base; una cella vuota non è un valore valido. Ricalcolare in Excel eventuali formule e salvare.
5. Collegare lo stesso piano e lo stesso percorso a **Bridge Read Section Excel**. Premere Run e collegare Section Table a **Bridge Segment Sections**, insieme al piano e alla proprietà base.
6. Le Section Rules entrano in Bridge FEM Members; gli ID collegano la stessa sezione a tutti gli elementi FEM del concio.

Cambiare mesh non richiede una nuova tabella. Cambiare i giunti richiede il riallineamento della tabella: il lettore rifiuta ID o intervalli incoerenti. La lettura è esplicita tramite Run e conserva la tabella importata nelle soluzioni successive.

Le quote degli assi sono riferimenti geometrici, **non baricentri ricavati automaticamente dalle sezioni**. Top Offsets restituisce la distanza dal baricentro all'ala superiore: usarla consapevolmente per le quote o gli offset. Le quattro varianti di fase devono mantenere gli stessi nodi.

## Analisi e fasi dell'esempio 50

La geometria alimenta il modello in acciaio e le varianti con proprietà omogeneizzate n∞, n0 e n da ritiro. Nel REV002 la classificazione sotto Dead governa gli stati fessurato/non fessurato dei due modelli meccanici. **Il ritiro resta non fessurato**, come concordato.

Condividere gli stessi riferimenti dei casi fra gli input Case delle azioni e **Bridge Analysis Cases**. Quest'ultimo riceve il modello *prima dell'analisi* e valida i nomi; la sua uscita entra in Analysis Results di Straus R3 Analysis. Appiattire la lista se vi si uniscono altri pannelli: rami diversi possono produrre più iterazioni dell'analisi. Non collegare gli oggetti azione a monte dell'analisi: si creerebbe una dipendenza circolare.

Dopo le analisi: azioni → fasi numerate → **Build BridGHe Model** → Super Element Check → controlli dei dettagli → grafici → report. Il ritiro primario e quello FEM secondario devono riferirsi alla stessa deformazione/azione; evitare di sommare due volte il contributo primario.

La verifica di sezione delle travi principali adotta l'ipotesi **N–Mx–Vy**: torsione, M2 e V1 sono esclusi. Le prove software non costituiscono la verifica completa di un ponte: restano da valutare secondo il progetto stabilità globale e locale, fatica, soletta, connessioni, appoggi, azioni e combinazioni applicabili.

## Migrazione e riproducibilità

I precedenti componenti monolitici sono nascosti dalla barra e sostituiti nei GHX distribuiti. Alcune classi restano registrate come adattatori per la migrazione e per confrontare i risultati con i vecchi dati; non sono il flusso da usare per nuovi modelli.

Principali modifiche al codice:

- `BridgeFabricationPlan.Create` centralizza i conci; `SimpleBridgeBuilder` usa lo stesso piano durante la mesh.
- `BridgeSegmentSections` e `BridgeSegmentWorkbook` accettano `IBridgeSegments`: non richiedono più una geometria discretizzata.
- La pubblicazione delle geometrie e l'assegnazione FEM mantengono il motore condiviso; cambiano le interfacce GH, non le formule di sezione.
- `BridgeAnalysisCasesComponent` usa `StrausResultSelection.Select` per validare i casi prima dell'esecuzione. Non cambia coefficienti, carichi o combinazioni.
- In gPC, `SectionSolver.GetLinearStressAnalysisResult` e `CompositeLinearStressSolver.Solve` hanno un overload per la precisione del solver; le firme precedenti restano disponibili. `HBridgeSection.Solve` ripete con tolleranza interna `1e-12` solo un calcolo che fallisce l'audit di equilibrio. La soglia dell'audit resta `1e-5`. Il confronto dei 238 valori della baseline di BridGHe è esattamente invariato.

Eseguire dalla root:

```powershell
dotnet build tests/BridGHe.Smoke -c Release
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression modular-bridge
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --migrate-bridge-examples
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --modular-bridge-native
```

La migrazione produce prima copie sotto `artifacts/modular-bridge/candidates`; `--publish` aggiorna gli originali dopo confronto e riapertura. Il test nativo legge la copia candidata e usa percorsi di calcolo separati, senza salvare i risultati FEM nei GHX. Prima di migrare file modificati a mano, conservarne una copia. Per le prove effettivamente eseguite consultare il rapporto di validazione associato a questa revisione.
