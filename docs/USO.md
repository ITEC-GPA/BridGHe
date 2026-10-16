# Uso e convenzioni

## Flusso del modello

1. Costruire materiali, sezioni e proprietà con i componenti esistenti. Collegare le proprietà a nodi, beam e plate; assegnare vincoli, carichi, gruppi e superelementi.
2. Assemblare il modello con `Build Model`, dichiarando le unità. Per il flusso MeCheck2 utilizzare **N–mm–MPa**; il controllo dei superelementi rifiuta modelli con unità differenti.
3. Esportare con `Straus7 R3 Export`, oppure eseguire `Straus7 R3 Analysis` scegliendo i casi richiesti. I risultati sono restituiti negli elementi di una copia del modello.
4. Per le fasi composte, analizzare i modelli con le proprietà di fase appropriate, combinarli mediante `Build MeCheck Model` e associare ogni caso a una sola fase: solo acciaio, lungo termine, istantanea, deformazione differita o istantanea.
5. Collegare i superelementi al verificatore MeCheck2 e i risultati ai componenti di estrazione/stampa.

Il verificatore adottato assume le azioni al **baricentro lordo della sezione della rispettiva fase**. Il motore gPC effettua il trasporto al baricentro efficace se necessario. Un modello con azioni riferite ad altri punti richiede un adattamento esplicito prima della verifica.

## Sezioni native Straus

I nuovi componenti hanno input dimensionali distinti, un output `Section` compatibile con `Beam Property`, e calcolano le proprietà tramite API R3. Richiedono la licenza API già al calcolo della sezione. Le dimensioni hanno le unità di lunghezza del modello; le aree sono L², le inerzie e J sono L⁴. Gli angoli BGL sono in gradi.

| Forma standard | Input dimensionali nell'ordine del componente |
|---|---|
| Circular Solid | D |
| Circular Hollow | D, T |
| Rectangle Solid | B, D |
| Rectangle Hollow | B, D, T1, T2 |
| Lipped Channel | B, D, L, T1, T2, T3 |
| Top Hat | B, D, L, T1, T2, T3 |
| I | B1, B2, D, T1, T2, T3 |
| T | B, D, L, T1, T2, T3 |
| Angle | B, D, T1, T2 |
| Z | B1, B2, D, T1, T2, T3 |
| Trapezoid Solid | B1, B2, D |
| Trapezoid Hollow | B1, B2, D, T1, T2 |
| Triangle Solid | B, D |
| Triangle Hollow | B, D, T1, T2 |
| Cruciform | B, D, T1, T2 |

Le sei geometrie BGL parametriche sono rettangolare cava, I, channel, T, angle e bulb flat; includono raggi di raccordo e inclinazioni dove previsti. Nomi, ordine e indici dei parametri sono in `migration/straus-sections.json`. La corrispondenza è stata ricavata dalle tabelle e dai disegni del manuale API R3 installato, pagine stampate 1003–1009. Per forme non familiari consultare quei disegni: `T1/T2` o `B1/B2` non devono essere reinterpretati per analogia con un'altra libreria.

`Straus BXS Section` legge un file BXS esistente. `Straus Library Section` legge un profilo BSL o BGL dalle librerie installate: nome libreria senza estensione e nome esatto del profilo. I materiali rimangono quelli del componente `Beam Property`; il materiale della libreria non viene importato.

Per BXS/BSL/BGL da libreria specificare `Length unit`: 0 m, 1 cm, 2 mm, 3 ft, 4 in. La stessa unità deve essere usata nel modello; una differenza genera errore. Conservare i file BXS e le librerie per le successive riesportazioni.

Restano `Generic Section` per proprietà assegnate numericamente e le sezioni da geometria della base, esportate mediante BXS. Le sezioni native nuove conservano geometria e proprietà nel modello, ma non hanno ancora l'anteprima solida estrusa; i moduli resistenti elastici non letti dalla chiamata nativa sono `NaN`.

## Carichi mobili

| Componente | Funzione |
|---|---|
| Moving Point Force | Forza a tre componenti: grouped o floating; coordinate locali/globali, adiacenza e flag centrifugo. |
| Moving Distributed Force | Distribuzione rettangolare: grouped, leading, trailing, full length o floating. |
| Moving Heat Source | Sorgente termica rettangolare; esportazione per il solutore termico Straus. |
| Moving Vehicle | Raggruppa gli oggetti di carico; corsia singola/doppia, verso, velocità, tempo iniziale, gruppo e corsie abilitate. |
| Moving Centrifugal Data | Espressioni `K0`, `K1`, eventuali limiti e unità per `Fc = K0 + K1 Fz`. |
| Moving Load Template | Veicoli, fattori di corsia, multicorsia, transizione e tolleranze di distribuzione. |
| Moving Library Template | Template MLT della libreria Straus installata, con impostazioni native. |
| Moving Load Path | Geometria straight/curved/quadratic, superficie flat/curved, discretizzazione e target. |
| Attach Moving Paths | Duplica il modello e associa i percorsi; crea i casi di carico mancanti. |
| Moving Static Analysis | Converte i percorsi selezionati in casi statici, esegue LSA e legge i risultati negli elementi. |

Sequenza: **Point/Distributed → Vehicle → Template → Path → Attach Moving Paths → Moving Static Analysis**. In alternativa Template può provenire dalla libreria MLT. Il percorso richiede i tre punti della convenzione nativa Straus; non devono essere collineari. Numerazione delle corsie da 1; elenco vuoto nel veicolo significa tutte le corsie. Le coordinate e le azioni devono rispettare le unità del modello, la velocità è L/s, il tempo è in secondi.

Le modalità disponibili espongono gli oggetti dell'API: non sono stati introdotti veicoli normativi o coefficienti di traffico inventati. La definizione dei modelli di carico di progetto resta esplicita nei dati del template o nelle librerie MLT disponibili.

`Generate static cases=true` abilita la conversione durante l'esportazione. Per leggere automaticamente questi casi utilizzare `Moving Static Analysis`; i nomi generati sono restituiti nell'output `Cases`. Si tratta di posizioni discrete: convergenza con il numero di divisioni e ricerca delle posizioni critiche devono essere controllate. Non è implementata l'analisi a linee/superfici d'influenza con inviluppo continuo.

Target supportati nel modello nuovo: Any, Beam e Plate. Entity Set e Brick sono esplicitamente rifiutati in esportazione perché questa trasposizione non crea set nativi né elementi brick. La sorgente termica si esporta, ma non viene risolta da `Moving Static Analysis`, che è strutturale. Con template MLT occorre verificare il contenuto della libreria: il componente non ispeziona tutti gli oggetti nativi del template.

## Convenzioni delle azioni

In `BeamForceResultModel`, i nomi storici `Mx/My` conservano rispettivamente i valori Straus `BM1/BM2`, cioè i momenti **nei piani** 1/2. Nel passaggio a gPC si usa `(M1, M2) = (BM2, -BM1)` e si applica il riferimento degli assi adottato dall'esportatore. Il momento positivo nel piano verticale produce compressione al lembo superiore nella convenzione del checker. Questa distinzione è verificata nella [documentazione ufficiale Strand7 R3](https://www.strand7.com/strand7r3help/Content/Topics/ResultsInterpretation/ResultsInterpretationBeamInternalForceConvention.htm); l'orientamento dei riferimenti è descritto in [Local and Principal Axes](https://www.strand7.com/strand7r3help/Content/Topics/Elements/ElementsBeamsPrincipalAxes.htm).

Le prove del convertitore sono eseguite; la verifica completa dei segni dopo esportazione, analisi e rilettura richiede la licenza nativa. Non modificare nuovamente segni o ordine delle componenti nei componenti downstream per compensare la conversione.
