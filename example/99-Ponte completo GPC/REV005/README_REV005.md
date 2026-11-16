# 99 REV005 — carichi delle fasi come lo script di riferimento aziendale

Aprire **[flusso-acciaio-misto_REV005.ghx](flusso-acciaio-misto_REV005.ghx)** con la Release aggiornata. Il file deriva dalla REV004, che resta invariata. Geometria (due campate 12 + 12 m, due travi a 3 m, soletta 5 m × 300 mm), sezioni, mesh, rigidezze dei quattro modelli G1, G2, Q e RITIRO, ritiro, verifiche, grafici e report sono quelli della REV004.

Cambiano i carichi delle fasi, con la regola dello script di riferimento aziendale per i ponti a sezione mista, e si aggiunge il tandem mobile dello schema 1.

## Carichi per fase

| Fase | Modello | Azione Excel | Caso FEM | Valore iniziale | Applicazione |
|---|---|---|---|---|---|
| 10 | G1 acciaio | `G1_ACCIAIO` | `Dead` | peso proprio delle carpenterie | coefficiente 1,15 × quello di `G1` nell'Excel: connessioni |
| 10 | G1 acciaio | `G1` | `G1_GETTO` | getto: 25 kN/m³ × 300 mm × 5.000 mm / 2 travi = **18,75 N/mm** per trave | il modello G1 non ha fasce: con due travi e sbalzi uguali la regola delle fasce dà metà impalcato per trave |
| 20 | G2 n∞ | `G2` | `G2_PERMANENTI` | combinazione FEM di cinque casi, coefficienti 1 | — |
| | | | `G2_PAV` | 25 kN/m³ × 150 mm = 3,75 kN/m² | fasce del modello G2 fra i cordoli |
| | | | `G2_CORD` | 25 kN/m³ × 200 mm = 5,0 kN/m² | 700 mm per lato, sugli sbalzi |
| | | | `G2_GR` | 1,5 kN/m ciascuno | bordo interno dei cordoli |
| | | | `G2_PAR` | 1,0 kN/m ciascuno | bordi |
| | | | `G2_FIN` | 1,5 kN/m per bordo | bordi |
| 30 | RITIRO | `RITIRO` | `RITIRO_FEM` | come REV004 | — |
| 40 | Q n0 | `Q` | `Q_TRAFFICO` | schema 1: 9 kN/m² sulla corsia 1 (3 m al cordolo sinistro), 2,5 kN/m² sull'area rimanente (0,6 m) | fasce del modello Q, su tutta la lunghezza |
| 40 | Q n0 | `WIND` | `WIND_1` | 2 kN/m² × (veicolo 3 m + trave 1,8 m + soletta 0,3 m) / 2 travi = 5,1 N/mm per trave | Y; 0,25 lungo X; 0,25 verso il basso |
| 40 | Q n0 | `FRENATURA` | `Q3_FRENATURA` | 0,6 · 2 · 300 + 0,10 · 9 · 3 · 24 = 424,8 kN | lungo X, ripartita sulle travi |
| 40 | Q n0 | `CENTRIFUGA` | `Q4_CENTRIFUGA` | 0,01 kN/m per trave | lungo Y |
| 40 | Q n0 | `TEMPERATURA` | `Q7_TEMPERATURA` | +35 °C, α ΔT = 1,2e-5 × 35 | travi, traversi e fasce |
| 45 | Q n0 + percorso | `Q_MOV_…` | posizioni del percorso | tandem schema 1 corsia 1, assi 2 × 300 kN, asse della corsia a 2.200 mm dal bordo sinistro | una posizione per combinazione |

Valori e geometria dei carichi sono slider o pannelli accanto al componente. Le fasce di ogni stazione sono raggruppate in un ramo, da destra a sinistra; le frazioni `a/L`, `b/L` sugli sbalzi derivano da cordolo e sbalzi. Le frazioni dei distribuiti di corsia sono pannelli scritti dal generatore per questa sezione trasversale. La lunghezza caricata della frenatura è uno slider (24.000 mm): aggiornarlo se cambia il tracciato.

## Combinazioni

**[Combinazioni_REV005.xlsx](Combinazioni_REV005.xlsx)** ha le azioni delle fasi 10–40: `G1_ACCIAIO`, `G1`, `G2`, `RITIRO`, `Q`, `WIND`, `FRENATURA`, `CENTRIFUGA`, `TEMPERATURA`. Le sei righe sono quelle della REV004. `G1_ACCIAIO` vale 1,15 × il coefficiente di `G1`; frenatura e centrifuga prendono quello di `Q`; vento e temperatura entrano in `DEMO_TOTALE` (1) e `DEMO_AMPLIFICATA` (1,5). Nella REV004 il vento aveva 0: ora ha anche componenti lungo X e verso il basso, che il verificatore N–Mx–Vy legge. Il tandem non è nell'Excel: `Bridge Moving Combinations` aggiunge a `DEMO_TOTALE` una posizione alla volta e passa la tabella completa a `Build BridGHe Model`. Coefficienti dimostrativi, non combinazioni normative.

## Differenze rispetto allo script di riferimento

- **Fattore 1,15**: lo script lo applica alla densità dell'acciaio; qui è il coefficiente di `G1_ACCIAIO` nell'Excel.
- **Getto**: lo script lo porta con le aste di soletta sul graticcio d'acciaio; qui il modello G1 ha solo l'acciaio e il getto è metà impalcato per trave, la stessa ripartizione per due travi simmetriche.
- **Lunga durata**: nello script il materiale viscoso riguarda solo le aste trasversali; qui G2 sta nel modello n∞ e il ritiro nel modello RITIRO, come nella REV004. Parametri del ritiro come la REV004.
- **Traffico**: lo script usa l'analisi mobile dello schema 1 su linee d'influenza e due disposizioni delle corsie; qui il tandem si muove sul percorso e i distribuiti sono statici su tutta la lunghezza. La carreggiata di 3,6 m ha una corsia convenzionale. Folla e fatica non riportate.
- **Frenatura** ripartita sulle travi, senza eccentricità verticale; **vento** con l'altezza della sezione delle travi; **temperatura** senza gradiente (lo script definisce il caso senza carichi).
- **Non riportati**: sisma, fatica, combinazioni normative.

## Esecuzione

1. Eseguire le quattro `Straus R3 Analysis` (G1 comprende `Dead`), poi **Moving Static Analysis** nel blocco Q.
2. **Run** su `Bridge Combination Excel` in lettura, poi su `Build BridGHe Model` e sui verificatori; report `Report_REV005.docx`.

I percorsi Straus sono in `REV005\calcoli`. Per analizzare una copia spostata aggiornare gli Output Path, come per le revisioni precedenti.

## Prove

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --example-reference-loads 99 --check
```

Riapre il GHX senza Straus e controlla valori, ordine delle fasce, risultanti dei carichi per caso, Excel, tandem, fasi e disposizione. Resoconto in [validation-reference-loads.json](validation-reference-loads.json).
