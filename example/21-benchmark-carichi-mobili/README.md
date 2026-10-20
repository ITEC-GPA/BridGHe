# Benchmark didattici dei carichi mobili

**Collaudo nativo del 20 ottobre 2026 superato:** 5 scenari, 80 posizioni e 5.377 confronti, nessun fallimento o warning del solutore. Report completo: [validation-native.json](validation-native.json). Sono verificati equilibrio, reazione destra, freccia di mezzeria e momenti a ogni stazione restituita; le tolleranze sono quelle originarie.

Preparazione senza licenza Straus:

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-moving-loads.ps1
```

Esecuzione con licenza Straus7 R3 API e modulo Moving Load:

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-moving-loads.ps1 -Native
# Per eseguire un solo scenario:
powershell -ExecutionPolicy Bypass -File tools/test-moving-loads.ps1 -Native -Scenario 04
```

Ogni esecuzione crea una cartella datata in `example/21-benchmark-carichi-mobili/calcoli`, con modello `.bgmodel`, riferimento `analytical.csv` e `report.json`. Solo l'opzione Native genera `.st7`, risultati e confronti FEM. Lo stato `prepared-offline-passed` conferma preparazione e archivi, **non** il superamento dell'analisi nativa. Un test fallito conserva il report e restituisce errore.

| Scenario | Obiettivo |
|---|---|
| 01-single-13 | Asse da 1000 N, 13 posizioni |
| 02-single-25 | Stesso asse, 25 posizioni; confronto con la stessa soluzione continua |
| 03-reverse | Inversione del percorso e andamento delle reazioni |
| 04-tandem | Due assi da 1000 N distanti 1500 mm; 13 divisioni, 16 posizioni compresi ingresso e uscita |
| 05-double-force | 2000 N, controllo della proporzionalità elastica |

Schema: trave appoggiata di 6000 mm, 12 elementi, E = 210000 MPa, I = 450000000 mm⁴. Rigidezza a taglio omessa per il confronto Euler–Bernoulli. Carichi verticali verso −Z, reazioni positive verso +Z. Nessun peso proprio. Sono carichi di benchmark, non LM1 o altre azioni normative.

Le forze di questi benchmark usano esplicitamente `Axes = 1`, sistema globale. Il primo collaudo nativo del 20 ottobre 2026 ha trovato un errore nel test inverso: mantenendo `Axes = 0` e lo stesso punto laterale, l'inversione del percorso ribalta la normale locale e quindi la forza globale. Il plugin esportava correttamente il dato locale, ma il riferimento analitico assumeva erroneamente una forza ancora verso −Z. Corretto il solo input del benchmark: la risultante delle reazioni passa da −1000 N a +1000 N e Uz cambia da positivo a negativo; il test inverso nativo è ora superato. Le convenzioni e i risultati dei modelli utente non sono modificati.

Per assi di forza positiva P alle ascisse aᵢ:

`Rdestra = Σ(P aᵢ / L)`; `Rsinistra = ΣP − Rdestra`.

`M(x) = Rsinistra x − Σ P max(0, x − aᵢ)`.

Lo spostamento di mezzeria si ricava per sovrapposizione dalla funzione di Green della trave appoggiata, implementata separatamente dai risultati FEM. Si confrontano reazioni, equilibrio, spostamenti e momenti a **ogni stazione restituita**. Non si presume che il campionamento contenga sempre la posizione esatta di ciascun asse. Tolleranze: 10⁻⁶ relative per le reazioni, 10⁻⁵ per momenti/spostamenti, con minimi assoluti dichiarati nel report.

Il secondo errore del benchmark riguardava il tandem: `Divisions` discretizza il percorso, ma non è necessariamente il numero dei casi generati. La conversione include ingresso e uscita del veicolo. Accorciare il percorso non serve a mantenere entrambi gli assi sulla trave e può alterare il trasferimento vicino ai bordi. Il percorso corretto coincide con tutta la luce, da 0 a 6000 mm.

Per queste prove di forze raggruppate si pone `h = L / Divisions`: alla posizione k, con k da zero, l'asse avanzato si trova a `(k + 0.5) h`, l'altro 1500 mm più indietro. Si considerano solo gli assi entro il percorso; il passaggio continua fino all'uscita dell'ultimo asse. Con 13 divisioni e interasse 1500 mm risultano **16 casi**, di cui 6 con un solo asse e 10 con entrambi. Nel percorso inverso si usa `x = L − x`. Il riferimento è calcolato dagli input, senza ricavare posizioni dalle reazioni misurate; il test nativo tandem è superato con queste convenzioni e con le tolleranze originali.

Il [manuale ufficiale della conversione](https://www.strand7.com/strand7r3help/Content/Topics/VISUALTools/ToolsConvertLoadPathsToLoadCases.htm) distingue i casi generati dalle divisioni del percorso; il [manuale dei template](https://www.strand7.com/strand7r3help/Content/Topics/LAYOUTS/LayoutsPaths.htm) descrive gli assi globali/locali e la spaziatura fissa delle forze raggruppate. La formula di campionamento sopra è stata verificata sperimentalmente su Straus R3.1.5 per questi cinque scenari, non è un contratto generale per tutti i template.

L'inviluppo di più posizioni non è uno stato simultaneo: non combinare indipendentemente i massimi di ogni componente per ricostruire una singola posizione del veicolo. Questi test non coprono ancora dinamica, centrifughe, superfici shell o ripartizione fra più corsie.
