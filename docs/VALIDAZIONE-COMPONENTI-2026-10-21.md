# Verifica degli esempi rispetto ai componenti attuali

Controlli eseguiti il 21 ottobre 2026 su Rhino 8, Release .NET 8 e Straus7 R3. Il perimetro iniziale comprende le definizioni distribuite nel catalogo prima delle nuove revisioni dell'esempio personale 99.

## Incongruenze corrette

- L'audit d'uso cercava nella vecchia cartella `examples` ed escludeva interi gruppi di componenti. Ora legge `example/catalog.json`, comprende tutte le classi di componenti e identifica le istanze attraverso i GUID degli oggetti GH.
- Il catalogo non elencava i due GHX aggiuntivi dell'esempio 47. Ora sono inclusi nella validazione.
- Sei istanze salvate avevano porte obsolete: due `Bridge Model Audit` nell'esempio 23 e un `Simple Bridge` in ciascuno degli esempi 29–32. I cinque file sono stati riaperti e risalvati con i componenti attuali, conservando dati persistenti e collegamenti.
- Il test di riapertura verificava soltanto il numero degli oggetti. Ora controlla anche ordine e identità delle sorgenti, numero/nome/accesso/opzionalità delle porte e ricalcola direttamente i GHX portabili salvati.
- Alcuni test riutilizzavano gli stessi file di esportazione. Ogni esecuzione ora ha una cartella distinta; la sovrascrittura intenzionale di PNG e report viene comunque provata nella stessa esecuzione.
- Gli errori delle regressioni sono stampati prima della chiusura dell'host nativo e propagati al chiamante. Lo script di build richiede i riepiloghi di completamento: exit code zero da solo non basta.

Queste modifiche riguardano esempi e infrastruttura di verifica; non cambiano gli algoritmi strutturali del plugin.

## Risultati effettivamente ottenuti

| Controllo | Esito |
|---|---|
| Compilazione Release | 0 errori, 0 avvisi; 24 file sincronizzati in `bin/Release` |
| Contratti dei componenti | 129 file di contratto, 183 GUID; nessun errore |
| Smoke principale | 223 controlli e 63 gruppi di regressione completati |
| Grafici e preview in processi separati | Entrambi i gruppi completati |
| Baseline numerica | 238 valori su 238 esattamente uguali; tolleranza di confronto assoluta/relativa 1e-12 |
| Riapertura dei GHX distribuiti | 48 documenti, 2.838 oggetti e 2.854 collegamenti conservati |
| Ricalcolo dei GHX portabili 23–45 | 23 documenti, nessun componente in errore, output presenti |
| Grafici pioli e irrigiditori | 91 asserzioni, inclusa sovrascrittura PNG con titolo modificato |
| Report Word tramite componente | Template, checklist, risultati, generazione esplicita, sovrascrittura e riapertura senza riscrittura superati |
| Benchmark FEM Straus | 10 esempi, 0 fallimenti, inclusi cinque scenari di costruzione per fasi |
| Carichi mobili Straus | 5 scenari, 5.377 misure superate |
| Fasi della sezione mista | 30 confronti, comprese deformazioni imposte a breve e lungo termine |
| Appoggi e collegamenti | Lettura di ritorno Straus di UCS, vincoli, molle e collegamenti rigidi superata |
| Appoggi distribuiti delle travi | 29 controlli nativi superati |
| Esempio 19 con risultati Straus | 378 risultati di sezione, nessun componente rosso |
| Interpolazione dell'esempio 19 | 12.192 componenti confrontate con risultati nativi alle stazioni comuni |

Nel confronto delle stazioni, gli errori massimi sono 6,37e-12 N per le forze, 9,54e-7 Nmm per i momenti flettenti e 1,14e-13 Nmm per la torsione. È compresa la stazione 879,044933748 mm che aveva provocato l'errore segnalato.

Il primo tentativo Straus è terminato con `St7Init -8` per chiave hardware assente. Dopo la disponibilità della licenza, tutte le verifiche native elencate sono state rieseguite con successo.

## Riproduzione e limiti del controllo

`tools/build.ps1 -Smoke` esegue smoke, regressioni, grafici, riapertura dei GHX e ricalcolo dei file portabili. Le regressioni mirate si eseguono con `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression NOME`.

I controlli nativi usano gli argomenti `--examples all`, `--moving-load-tests all --native`, `--bridge-native`, `--workflow-stations-native`, `--bridge-bearings-native`, `--bridge-connections-native` e `--beam-support`.

I log della sessione sono in `artifacts/current-component-audit/`. I report numerici nativi sono nelle cartelle `calcoli` degli esempi e in `example/00-documentazione/validazioni/esecuzioni/20261021-222728482/`.

La riapertura di 48 file non equivale al ricalcolo FEM di tutti i ponti del catalogo: sono stati ricalcolati i benchmark e ripercorsi i flussi esplicitamente indicati. L'audit trova 81 componenti nei GHX correnti e 93 assenti; questa misura non descrive la copertura numerica dei test. La cartella personale 99 è stata esclusa da questo controllo iniziale e viene affrontata con revisioni dedicate su richiesta dell'utente.
