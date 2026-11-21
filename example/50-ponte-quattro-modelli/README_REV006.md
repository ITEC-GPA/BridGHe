# 50 REV006 — il mobile nelle verifiche (BridGHe 0.7.0, Concomitanti dalla 0.7.1)

Aprire **[50-ponte-quattro-modelli_REV006.ghx](50-ponte-quattro-modelli_REV006.ghx)** con la Release 0.7.1. Il file deriva dal REV005, che resta, con il comando `--example-envelope 50`. Geometria, sezioni, carichi, fasi, combinazioni e stazioni sono quelli del REV005.

## Cosa cambia

**Bridge Moving Envelope** fra la tabella delle combinazioni mobili e Build BridGHe Model, con la Value List «Mobile nelle verifiche»:

| Scelta | Combinazioni del mobile | Uso |
|---|---|---|
| Posizioni | una per posizione (31): i risultati del REV005 | verifica |
| Inviluppo | 8 angoli N±, V±, M± in ogni stazione, sempre a favore di sicurezza | dimensionamento |
| **Concomitanti** (impostata dalla 0.7.1) | massimo e minimo di N, V, M con le sollecitazioni della stessa posizione (6) | dimensionamento |

Nella 0.7.0 la scelta di partenza era Posizioni; dalla 0.7.1 è Concomitanti, il calcolo più leggero. Per la verifica finale scegliere Posizioni.

Per cambiare scelta: Value List, poi Run su Build BridGHe Model e sui check.

## Verifica (nativo con Straus7)

| | Righe dei risultati | Utilizzo massimo delle tensioni | Rapporto massimo delle verifiche locali |
|---|---:|---:|---:|
| Posizioni | 16.776, identiche al REV005 | 0,58324 | 3,0338 |
| Inviluppo | 6.058 | 0,58324, stessa sezione (G3, asta 150) | 3,0372 |
| Concomitanti | 5.126 | 0,58324, stessa sezione | 3,0368 |

Con Inviluppo e Concomitanti le reazioni degli irrigiditori agli appoggi sono la compressione massima fra le posizioni: il rapporto locale massimo è un po' più alto.
