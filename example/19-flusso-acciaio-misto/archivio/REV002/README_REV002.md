# Revisione del file utente: matching del ritiro e input della sezione

`19-flusso-acciaio-misto_REV002.ghx` deriva dal file REV001 fornito dall'utente. Gli originali REV001 e `Combo.xlsx` sono conservati. `Combo_REV002.xlsx` è una copia identica del foglio delle combinazioni, senza coefficienti modificati.

## Perché Build BridGHe Model diventava rosso

La Value List dell'ultimo ingresso veniva popolata dal catalogo delle deformazioni imposte della soletta, mentre la validazione di quell'ingresso richiede casi con sollecitazioni FEM. Nel file era selezionato `5-RITIRO_PRIMARIO`. La lista è stata corretta nel codice; il messaggio di errore distingue ora i due tipi di caso.

Nella REV002 il matching è:

| Caso | Ingresso del merge | Fase interna |
|---|---|---|
| `1-G1_GETTO` | Only Steel / Inf Load Cases | OnlySteel |
| `2-G2_PERMANENTI` | Ninf Load Cases | NInfinite |
| `3-Q_TRAFFICO`, `3-WIND_1` | N0 Load Cases | NInstant |
| `4-RITIRO_FEM` | ultimo ingresso, n ritiro FEM Load Cases | NShrinkage |
| `5-RITIRO_PRIMARIO` | Ninf strain Load Cases | StrainInfinite |

`4-RITIRO_FEM` è stato tolto dalla selezione N0. Il caso primario è collegato alla lista delle deformazioni differite. Il prefisso 5 è corretto per questa revisione: la deformazione primaria arriva dal quinto modello della lista.

**Nel foglio manca la colonna `4-RITIRO_FEM`.** Le cinque combinazioni fornite includono il ritiro primario quando il relativo coefficiente è diverso da zero, ma escludono le sollecitazioni FEM da ritiro. Il matching non crea automaticamente i coefficienti. Aggiungere quella colonna e definirne i coefficienti per considerare anche il contributo globale; qui non sono stati introdotti coefficienti normativi.

## Compatibilità di Composite Beam Property

Nel GHX `n ritiro = 12` è sull'ultimo ingresso, mentre il sorgente locale lo aveva spostato prima di `Consider Concrete` e `Consider Rebars`. Grasshopper ricarica i parametri statici per posizione: senza migrazione si perdevano i dati e veniva usato n∞ = 18.

Il componente ora riconosce l'ordine salvato prima della lettura, conserva i GUID delle porte e legge i tre valori per nome. Le opzioni booleane vuote usano il default dichiarato `true`. Il valore di omogeneizzazione effettivo torna quindi a **12**. Nei file già coerenti i risultati restano invariati.

Con gli stessi risultati FEM memorizzati e le stesse cinque combinazioni, la correzione 18 → 12 cambia le tensioni da ritiro: differenze massime assolute di 0.186351 MPa nel calcestruzzo superiore, 4.403887 MPa nell'armatura superiore, 4.030579 MPa nell'armatura inferiore e 1.071300 MPa nell'acciaio inferiore. Sono differenze sul file fornito, non tolleranze o limiti di verifica.

## Riproduzione e limiti

Richiede la Release aggiornata di BridGHe, Rhino 8 e TT Toolbox per `Read Excel Sheet`. Il pannello del percorso punta a `Combo_REV002.xlsx` in questo checkout: aggiornarlo se si spostano i file. Avviare la lettura Excel, poi **Run** su `Build BridGHe Model`, **Run** su `Super Element Check` e **Draw** sui plot.

Il replay del GHX con risultati memorizzati ha elaborato **12 beam, 6 casi, 5 combinazioni e 275 risultati**. Tutti i **223 campioni di sollecitazione** presenti nei modelli sorgenti sono conservati esattamente dal merge, sulle sei componenti: differenza massima zero. I coefficienti Excel sono stati letti dal file XLSX e forniti direttamente al merge nel processo di test; non è stata automatizzata l'interfaccia Excel/TT Toolbox.

Il modello `RITIRO_FEM` è stato anche **ricalcolato con Straus7 R3** e n = 12; la sua cache nella REV002 è aggiornata. La precedente I11 memorizzata era 8.3425446886×10¹⁰ mm⁴ (n = 18), quella corretta è 8.9482670772×10¹⁰ mm⁴. Le proprietà delle altre tre analisi coincidono già con gli input e le loro cache sono conservate. Il nuovo file nativo è generato in `artifacts/revision-native/RITIRO_FEM.st7`. La mancanza del caso FEM nella matrice delle combinazioni resta esplicita: il ricalcolo non lo inserisce automaticamente.

Regressioni dedicate: `bridge-plots`, `plot-preview`, `bridge-merge-origin`, `composite-property-phases`. Quest'ultima controlla entrambi gli ordini degli input, il valore 12, le opzioni booleane, i tipi e i GUID dopo serializzazione. La prova del flusso non certifica le combinazioni fornite né costituisce una verifica strutturale completa del ponte.
