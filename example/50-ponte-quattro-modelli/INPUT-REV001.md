# REV001: tracciato, appoggi e sezioni da Excel

Aprire [50-ponte-quattro-modelli_REV001.ghx](50-ponte-quattro-modelli_REV001.ghx) con la Release aggiornata. Il file conserva curva, sezioni e valori salvati dall'utente, distribuiti fra le nuove [definizioni modulari](../../docs/FLUSSO-MODULARE.md). La procedura vale anche per il REV002.

## Tracciato e progressive degli appoggi

1. Impostare la curva nel parametro **TRACCIATO [mm]** con `Set one curve`. Deve essere aperta, valida e in millimetri.
2. Nel pannello **APPOGGI [mm]** inserire le distanze lungo la curva, una per riga. Sono lunghezze d'arco 3D, non coordinate X o parametri della curva. Invertendo la curva cambia l'origine delle progressive.
3. **Bridge Support Stations** ordina e accorpa le progressive entro tolleranza. `End Supports=True` aggiunge gli estremi; con `False` specificare tutti gli appoggi, lasciando eventualmente gli sbalzi. Una lista vuota permette una suddivisione in campate uguali tramite Span Count.
4. Le progressive normalizzate entrano in **Bridge Support Lines**, insieme agli sghembi. I vincoli meccanici sono assegnati successivamente da Bridge Bearings.
5. **Bridge Girder Axes**, **Bridge Slab Layout** e **Bridge Solid/Truss Diaphragms** controllano separatamente travi, soletta e diaframmi. **Bridge Geometry Assembly** li unisce; **Bridge Grillage Mesh** crea le aste.

La curva personalizzata ha precedenza su quella generata da campate/raggio. Se si usa il ramo parametrico, Bridge Span Stations e Bridge Alignment Curve preparano lunghezze e curva coerenti.

## Conci e mesh sono distinti

**Bridge Fabrication Segments** restituisce il numero dei conci, gli ID e gli intervalli prima della mesh. Gli oggetti **Bridge Joint Stations** permettono giunti comuni o per trave. I dati precedenti ad albero passano attraverso Bridge Joint Tree.

In assenza di giunti espliciti per una trave, Maximum Segment Length crea parti uguali non più lunghe del valore richiesto; zero genera un solo concio. I giunti espliciti sostituiscono questa suddivisione. Gli appoggi non diventano automaticamente giunti costruttivi.

**Bridge Mesh Settings → Maximum Element Length** controlla invece la discretizzazione FEM; un concio può contenere molte aste. Gli elementi trasversali della soletta hanno passo o progressive proprie e le travi vengono spezzate ai relativi attacchi.

## Excel: creazione e lettura separate

Il file già collegato è [Sezioni_conci_REV001.xlsx](Sezioni_conci_REV001.xlsx). Il REV002 usa il proprio file omonimo.

1. Per un nuovo ponte collegare Fabrication Plan a **Bridge Section Excel Template** e indicare File. Premere **Run** per creare il template; Overwrite deve essere esplicitamente abilitato per sostituire un file esistente.
2. Compilare il foglio Sezioni. Mantenere ID, progressive e tutte le righe; `*` eredita la dimensione dalla proprietà base, mentre una cella vuota è un errore. Ricalcolare in Excel le eventuali formule e salvare.
3. Collegare lo stesso piano e lo stesso percorso a **Bridge Read Section Excel**, già presente nell'esempio. Premere **Run**.
4. Section Table entra in **Bridge Segment Sections**, insieme a Fabrication Plan e Base Property. Le regole vengono assegnate alle aste tramite **Bridge FEM Members**; il numero di righe dipende dai conci fisici, non dalla mesh.
5. Cambiando giunti o numero di travi, riallineare il foglio. Il lettore blocca intervalli, ID o righe incoerenti. Cambiare solo la discretizzazione FEM non richiede nuove righe.

Il lettore conserva la tabella importata fra le soluzioni e nella riapertura; una modifica del file Excel richiede un nuovo Run. Per ricalcolare il ponte dopo modifiche, rieseguire tutte le analisi di fase interessate.
