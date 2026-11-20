# Riferimenti del gate nativo, esempi 50 REV004 e 99 REV005 (0.5.0)

Registrati con l'OK dell'utente del 20/11/2026 («Sì, procedi»).

- **Prima**: riferimenti della 0.4.2 (`native-gate-042`, `P2B\results\t042-native-50` e `-99`), confermati identici dalla 0.4.3.
- **Dopo**: nativi della 0.5.0 sul commit `46da3bc`, set BA ufficiale (manifest `77DAE25D…`).
- I valori attesi sono in `references.json`. Le righe complete sono fuori dal repository, in `P2B\results\t050-native-50` e `t050-native-99`.

Le definizioni usano ora Concrete Slab Section con passo e diametri, con le stesse aree delle file di barre:
- nel 50: Ø16/16, passo 172,3388 mm;
- nel 99: Ø16/16, passo 100 mm.

La sezione delle verifiche ha circa larghezza/passo barre per fila del diametro vero, invece di 3 barre equivalenti; la sezione dell'analisi non cambia. Per questo l'ingresso di tutte le righe di verifica è diverso (la definizione della sezione), mentre i valori cambiano solo alla sesta cifra.

## Esempio 50: `example50-rev004-native`

| Controllo | Prima (0.4.2) | Dopo (0.5.0) |
|---|---:|---:|
| Righe di verifica nell'impronta | 68.184 | 68.184 |
| Impronta delle righe (SHA-256) | `C94CEC82F0CBEACB…` | `8E5A71DB75CBD721…` |
| Righe con la stessa chiave: uguali / diverse | 0 / 66.528 | |
| Righe solo prima / solo dopo | 1.656 | 1.656 |
| Utilizzo massimo delle tensioni | 0,58323519 | 0,58323679 (stesso punto) |
| Rapporto massimo delle verifiche locali | 3,03381163 | 3,03381163 (stesso punto) |
| Massimo scarto: utilizzo / rapporto / tensione | | 3,2·10⁻⁶ / 1,7·10⁻⁶ / 5,4·10⁻⁴ MPa |
| Punti di tensione oltre 1 / verifiche locali oltre 1 | 0 / 321 | 0 / 321 |
| Conteggi P2K (analisi, fasi, travi, nodi, posizioni, combinazioni, verifiche, dettagli, grafici) | 5, 4, 498, 488, 31, 36, 33.444, 3, 6 | uguali |
| Report Word: allegati / risultati | 36 / 34.740 | 36 / 34.740 |

Le 1.656 righe «solo prima / solo dopo» sono le stesse verifiche in 23 stazioni calcolate, cioè massimi e cambi di regime, non stazioni della griglia. La loro ascissa cambia al più di 6·10⁻⁶ mm, per esempio G2, trave 107, s = 71,7376466 → 71,7376478 mm.

## Esempio 99: `modular-flusso-acciaio-misto_REV005`

| Controllo | Prima (0.4.2) | Dopo (0.5.0) |
|---|---:|---:|
| Righe di verifica nell'impronta | 15.004 | 15.004 |
| Impronta delle righe (SHA-256) | `9DE143576C252CF9…` | `867E340DFD8D062E…` |
| Righe con la stessa chiave: uguali / diverse | 0 / 15.004 (ingresso) | |
| Utilizzo massimo delle tensioni | 0,17139562 | uguale (scarto ≤ 1·10⁻¹⁶) |
| Rapporto massimo delle verifiche locali | 0,99759991 | uguale |
| Massimo scarto dei rapporti | | 1,2·10⁻⁶ |
| Tensioni nei punti 5 e 8 | | uguali |
| Punti di tensione oltre 1 / verifiche locali oltre 1 | 0 / 0 | 0 / 0 |
| Conteggi P2K (fasi, combinazioni, verifiche, dettagli, grafici, posizioni mobili) | 5, 31, 6.758, 6, 10, 25 | uguali |
