# 50 REV004 — carichi delle fasi come lo script di riferimento aziendale

Aprire **[50-ponte-quattro-modelli_REV004.ghx](50-ponte-quattro-modelli_REV004.ghx)** con la Release aggiornata. Il file deriva dal REV003, che resta invariato. Geometria, sezioni, mesh, appoggi, rigidezze dei quattro modelli, mappa fessurata da `Dead`, ritiro, verifiche, grafici e report sono quelli del REV003. **[Sezioni_conci_REV004.xlsx](Sezioni_conci_REV004.xlsx)** è una copia del foglio REV003.

Cambiano soltanto i carichi delle fasi. Seguono la regola dello script di riferimento aziendale per i ponti a sezione mista: getto della soletta sulla sola sezione d'acciaio, permanenti portati e ritiro a lunga durata, traffico e altre azioni variabili a breve durata.

## Carichi per fase

| Fase | Modello | Azione | Valore iniziale | Applicazione |
|---|---|---|---|---|
| 10 | M01 acciaio | `M01_ACCIAIO_PESO` | peso proprio delle carpenterie (caso `Dead`) | coefficiente **1,15** nelle combinazioni: incremento per le connessioni |
| 10 | M01 acciaio | `M01_ACCIAIO_SOLETTA` | getto: 25 kN/m³ × 300 mm = 7,5 kN/m² | su ogni fascia × larghezza tributaria, come nel REV003 |
| 10 | M01 acciaio | `M01_ACCIAIO_TRAVI` | 0 N/mm | casseri o altri carichi di costruzione: lo script non ne ha |
| 20 | M02 n∞ | `M02_NINF_G2_PAV` | pavimentazione 25 kN/m³ × 150 mm = 3,75 kN/m² | fasce fra i cordoli |
| 20 | M02 n∞ | `M02_NINF_G2_CORD` | cordoli 25 kN/m³ × 200 mm = 5,0 kN/m² | 700 mm per lato, sugli sbalzi |
| 20 | M02 n∞ | `M02_NINF_G2_GR` | guard-rail 1,5 kN/m ciascuno | forza su ogni fascia di sbalzo al bordo interno del cordolo |
| 20 | M02 n∞ | `M02_NINF_G2_PAR` | parapetto 1,0 kN/m ciascuno | forza al bordo |
| 20 | M02 n∞ | `M02_NINF_G2_FIN` | finiture, velette, impianti 1,5 kN/m per bordo | forza al bordo |
| 30 | M03 n0 | `M03_N0_Q1_UDL` | schema 1: 9 kN/m² sulla corsia 1, 2,5 kN/m² sul resto | corsia 1 di 3 m accanto al cordolo sinistro, su tutta la lunghezza |
| 30 | M03 n0 | `Q_MOV_…` | tandem schema 1: corsia 1 assi 2 × 300 kN, corsia 2 assi 2 × 200 kN | analisi mobile; asse della corsia 1 a 2.200 mm dal bordo sinistro, corsia 2 a 3.000 mm verso destra |
| 30 | M03 n0 | `M03_N0_Q3_FR` | frenatura 0,6 · 2 · 300 + 0,10 · 9 · 3 · L, entro 180–900 kN (522 kN per L = 60 m) | lungo X, ripartita sulle travi |
| 30 | M03 n0 | `M03_N0_Q4_CENTR` | centrifuga 0,01 kN/m per trave | lungo Y |
| 30 | M03 n0 | `M03_N0_Q5_VENTO` | vento 2 kN/m² × (veicolo 3 m + trave + soletta) = 11,6 kN/m | ripartito sulle travi: Y, 0,25 lungo X, 0,25 verso il basso |
| 30 | M03 n0 | `M03_N0_Q7_TEMP` | temperatura uniforme +35 °C | deformazione impressa α ΔT = 1,2e-5 × 35 su travi, traversi, controventi e fasce |
| 40 | M04 ritiro | `M04_NSH_RITIRO` | come REV003 | — |

Tutti i valori sono slider o pannelli sul canvas, accanto al componente che li usa. Le fasce di soletta di ogni stazione sono raggruppate in un ramo, da destra a sinistra: sbalzo destro, campi fra le travi, sbalzo sinistro. Le frazioni `a/L` e `b/L` dei carichi sugli sbalzi si calcolano dalla larghezza del cordolo e degli sbalzi. Le frazioni dei carichi distribuiti di corsia sono pannelli scritti dal generatore per la sezione trasversale di questo file: se si cambiano travi, sbalzi o cordoli vanno aggiornate.

## Combinazioni

La tabella `COMBINAZIONI TEST` ha una colonna per azione. Le cinque righe restano quelle del REV003: le quattro `TEST_Mxx` isolano un modello, `DEMO_CUMULATIVA` somma tutto con coefficiente 1. Il peso proprio dell'acciaio ha 1,15 in tutte le righe in cui compare. Per ogni posizione del tandem si aggiunge, come nel REV003, una combinazione `DEMO_CUMULATIVA + Q_MOV_…`. Non sono combinazioni normative: coefficienti γ e ψ restano da scegliere.

## Differenze rispetto allo script di riferimento

- **Fattore 1,15.** Lo script lo applica alla densità dell'acciaio. Qui è il coefficiente dell'azione `M01_ACCIAIO_PESO`, perché il componente Steel non ha un ingresso di densità.
- **Lunga durata.** Lo script calcola le fasi con viscosità nel tempo, ma lega il materiale viscoso solo alle aste trasversali di soletta: nelle travi la sezione resta al rapporto modulare istantaneo e il ritiro non agisce in senso longitudinale. Qui i G2 stanno nel modello n∞ e il ritiro nel modello M04, come nel REV003.
- **Ritiro.** Parametri come il REV003 (fck 32 MPa, 7–365 giorni, due facce esposte). Lo script usa fck 35 MPa, 3 giorni, 10.000 giorni e una sola faccia esposta.
- **Traffico.** Lo script usa l'analisi a carichi mobili dello schema 1 con le zone sfavorevoli delle linee d'influenza, su due disposizioni delle corsie (da destra e da sinistra). Qui i tandem si muovono su un percorso e i distribuiti sono statici su tutta la lunghezza: per le campate continue questo non dà il massimo momento in campata. Una sola disposizione delle corsie: l'impalcato è simmetrico. La carreggiata di 8,6 m ha due corsie convenzionali: nessun tandem sulla corsia 3. Folla sulle corsie pedonali e fatica non sono riportate (nello script la folla è disattivata).
- **Frenatura.** Lo script la applica alla linea della corsia 1, alla quota della soletta. Qui è ripartita sulle travi, senza eccentricità verticale.
- **Vento.** Lo script usa l'altezza del primo concio; qui quella della sezione base. La componente verticale è −Z globale (asse locale z nello script).
- **Temperatura.** Nessun gradiente: lo script definisce il caso senza carichi. Il cls delle fasce ha α = 1,2e-5 come lo script, anche se il materiale fittizio delle fasce ha 1e-5.
- **Non riportati:** sisma (analisi spettrale), schema di fatica, combinazioni di carico (lo script non ne definisce).

## Esecuzione

1. **Run** su `Bridge Read Section Excel` per leggere il foglio REV004.
2. Eseguire **M01**: `Dead` costruisce la mappa delle rigidezze ed è anche l'azione `M01_ACCIAIO_PESO`. Fino ad allora i selettori di M03 segnalano i casi delle travi mancanti (frenatura, centrifuga, vento).
3. Eseguire **M02, M03, M04**, poi **Moving Static Analysis** nel blocco M03.
4. **Run** su `Build BridGHe Model`, quindi su `Super Element Check`; grafici e report come nel REV003, destinazione `Report_50_REV004.docx`.

I file Straus vanno in `calcoli/REV004`. Il GHX distribuito non contiene risultati FEM e non lancia analisi all'apertura.

## Prove

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --example-reference-loads 50 --check
```

La prova riapre il GHX senza Straus e controlla valori, alberi delle fasce, risultanti dei carichi applicati per caso, tandem e percorso, fasi, colonne della tabella e disposizione da sinistra a destra. Il resoconto è in [REV004-validation.json](REV004-validation.json). Il generatore `--example-reference-loads 50` rifiuta di sovrascrivere una revisione già esistente.
