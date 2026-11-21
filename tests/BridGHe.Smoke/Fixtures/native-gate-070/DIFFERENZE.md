# Riferimenti del gate nativo, esempi 50 REV006 e 99 REV007 (0.7.0)

Le definizioni correnti del catalogo passano da 50 REV005 / 99 REV006 a 50 REV006 / 99 REV007. Sono le decisioni del 21/11/2026: Bridge Moving Envelope fra le combinazioni mobili e Build BridGHe Model, con la Value List su Posizioni.
- Ogni esempio è provato nei tre modi del mobile; per Inviluppo e Concomitanti il nativo cambia solo la Value List in memoria.
- I riferimenti della 0.6.0 (`native-gate-060`) restano per le definizioni precedenti.
- Nativi della 0.7.0 sul commit `56dc2e8`, set BA ufficiale (manifest `77DAE25D…`).
- I valori attesi sono in `references.json`. Le righe complete sono fuori dal repository, in `P2B\results\t070-native`.

## Esempio 50 (REV006)

| Controllo | 0.6.0 (REV005) | Posizioni | Inviluppo | Concomitanti |
|---|---:|---:|---:|---:|
| Combinazioni del modello | 36 | 36 | 13 | 11 |
| Righe di verifica nell'impronta | 16.776 | 16.776, identiche | 6.058 | 5.126 |
| Impronta delle righe (SHA-256) | `4C574AA783AB4825…` | `4C574AA783AB4825…` | `9AFA19506359DB22…` | `867C3C27B19EDDAC…` |
| Super Element Check (verifiche di sezione) | 12.960 | 12.960 | 4.680 | 3.960 |
| Detail «Sezioni e pioli» | 2.520 | 2.520 | 910 | 770 |
| Irrigiditori agli appoggi / ai giunti | 432 / 864 | 432 / 864 | 156 / 312 | 132 / 264 |
| Utilizzo massimo delle tensioni | 0,58323679 | 0,58323679 | 0,58324442 | 0,58323679 |
| Punto del massimo | G3, asta 150, s = 1000 mm | uguale | uguale, angolo N−V+M− | uguale, Mmin |
| Rapporto massimo delle verifiche locali | 3,03381163 | 3,03381163 | 3,03719704 | 3,03680092 |
| Punto del rapporto massimo | irrigiditore all'appoggio, asta 151, saldature alle flange | uguale | uguale, N−V+M− | uguale, Vmax |
| Punti di tensione oltre 1 / verifiche locali oltre 1 | 0 / 321 | 0 / 321 | 0 / 106 | 0 / 82 |
| Report Word: allegati / risultati | 4 / 3.816 | 4 / 3.816 | 2 / 1.378 | 2 / 1.166 |

Il rapporto locale massimo cresce un poco con Inviluppo e Concomitanti. Per le reazioni degli irrigiditori agli appoggi conta infatti la compressione massima fra le posizioni, anche se non è della stessa posizione delle sollecitazioni.

## Esempio 99 (REV007)

| Controllo | 0.6.0 (REV006) | Posizioni | Inviluppo | Concomitanti |
|---|---:|---:|---:|---:|
| Combinazioni del modello | 31 | 31 | 14 | 12 |
| Righe di verifica nell'impronta | 5.952 | 5.952, identiche | 2.688 | 2.304 |
| Impronta delle righe (SHA-256) | `EAFE2960CC9B0E7C…` | `EAFE2960CC9B0E7C…` | `1899C3FE50E8927A…` | `F80AC6E86D77198B…` |
| Super Element Check (tutte le travi / G2) | 1.488 / 1.488 | uguali | 672 / 672 | 576 / 576 |
| Detail dei pioli (fasi / G2) | 744 / 744 | uguali | 336 / 336 | 288 / 288 |
| Irrigiditori (appoggi, intermedi, appoggi G2, intermedi G2) | 124, 620, 124, 620 | uguali | 56, 280, 56, 280 | 48, 240, 48, 240 |
| Utilizzo massimo delle tensioni | 0,17139562 | uguale | uguale | uguale |
| Punto del massimo | Pioli G2, asta 37, s = 0, DEMO_AMPLIFICATA | uguale | uguale | uguale |
| Rapporto massimo delle verifiche locali | 0,99759991 | uguale | uguale | uguale |
| Verifiche oltre 1 | 0 | 0 | 0 | 0 |

Nel 99 il massimo è in una combinazione senza mobile: i tre modi danno gli stessi massimi.
