# Riferimenti del gate nativo, esempi 50 REV005 e 99 REV006 (0.6.0)

Le definizioni correnti del catalogo passano da 50 REV004 / 99 REV005 a 50 REV005 / 99 REV006. Sono le decisioni del 20/11/2026: Super Element Check a 2 stazioni per asta, Detail Check sulle sezioni notevoli. I riferimenti della 0.5.0 (`native-gate-050`) restano per le definizioni precedenti.

- Nativi della 0.6.0 sul commit `078b056`, set BA ufficiale (manifest `77DAE25D…`).
- I valori attesi sono in `references.json`. Le righe complete sono fuori dal repository, in `P2B\results\t060-native-50` e `t060-native-99`.

## Esempio 50: `example50-rev005-native`

| Controllo | 0.5.0 (REV004) | 0.6.0 (REV005) |
|---|---:|---:|
| Righe di verifica nell'impronta | 68.184 | 16.776 |
| Impronta delle righe (SHA-256) | `8E5A71DB75CBD721…` | `4C574AA783AB4825…` |
| Super Element Check (verifiche di sezione) | 33.444 | 12.960 |
| Detail «Sezioni e pioli» | 33.444 | 2.520 |
| Irrigiditori agli appoggi / ai giunti | 432 / 864 | 432 / 864, con lo stesso numero di verifiche per voce |
| Utilizzo massimo delle tensioni | 0,58323679 | 0,58323679 (stesso punto: G3, asta 150, s = 1000 mm) |
| Rapporto massimo delle verifiche locali | 3,03381163 | 3,03381163 (stesso punto: irrigiditore all'appoggio, asta 151) |
| Punti di tensione oltre 1 / verifiche locali oltre 1 | 0 / 321 | 0 / 321 |
| Interazione N–M–V (EN 1993-1-5 §7.1), massimo | 0,6789 (G3, asta 151, s = 0) | 0,6789 (stesso punto) |
| Report Word: allegati / risultati | 36 / 34.740 | 4 / 3.816 |
| Conteggi P2K (analisi, fasi, travi, nodi, posizioni, combinazioni, dettagli, grafici) | 5, 4, 498, 488, 31, 36, 3, 6 | uguali |

La verifica N–M–V c'è solo dove il taglio è alto, vicino agli appoggi. Nella 0.5.0 compariva in 95 stazioni, nella 0.6.0 in 6. Le 89 stazioni in meno sono interne alle aste vicine agli appoggi e hanno valori più bassi del massimo, fino a 0,6534. Il massimo è all'appoggio, che è una sezione notevole.

## Esempio 99: `modular-flusso-acciaio-misto_REV006`

| Controllo | 0.5.0 (REV005) | 0.6.0 (REV006) |
|---|---:|---:|
| Righe di verifica nell'impronta | 15.004 | 5.952 |
| Impronta delle righe (SHA-256) | `867E340DFD8D062E…` | `EAFE2960CC9B0E7C…` |
| Super Element Check (tutte le travi / G2) | 3.317 / 3.441 | 1.488 / 1.488 |
| Detail dei pioli (fasi / G2) | 3.317 / 3.441 | 744 / 744 |
| Irrigiditori (appoggi, intermedi, appoggi G2, intermedi G2) | 124, 620, 124, 620 | uguali |
| Utilizzo massimo delle tensioni | 0,17139562 | uguale (stesso punto: Pioli G2, asta 37, s = 0) |
| Rapporto massimo delle verifiche locali | 0,99759991 | uguale (stesso punto: appoggi G2, saldature alle flange) |
| Verifiche oltre 1 | 0 | 0 |

## Correzioni trovate dal gate nativo

- Bridge Notable Stations: con i risultati collegati dà i punti delle sole travi che i risultati coprono. Prima «Pioli G2», che legge il Super Element Check di G2, riceveva anche i punti di G1 e Bridge Detail Stations si fermava.
- Super Element Check: i percorsi delle uscite tengono l'indice della stazione fra tutte le stazioni dell'asta. Prima, con 2 stazioni su 5, i grafici disegnavano la fine dell'asta alla seconda stazione e gli irrigiditori agli appoggi del 99 non trovavano la stazione a 24 m.
