# Ingressi delle prove F7 (compito 179)

`F05b-schema2-sviluppo.ghx`, `F09a-strain-obsoleto-cs-stirato.ghx` e `F09b-legacy-0.3.0.ghx` sono gli ingressi di `--f7-readers` (F05b) e di
`--f7-local` (F09a, F09b). Sono i file della prova F7 del 14/11/2026 (`gpc-f6-claude\F7-20261114\file\F05` e `F09`, con gli SHA-256 della
sua `PROVENIENZA.md`), copiati senza modifiche il 18/11/2026 per la 0.4.2 (consiglio 9 della parte 1, approvato dall'utente): un checkout
nuovo non ha `artifacts\179\input`, e senza questi file F7 falliva (F05b) o saltava in silenzio (F09).

| File | Byte | SHA-256 |
|---|---:|---|
| `F05b-schema2-sviluppo.ghx` | 25.360 | `B5C992953DBD3337D049E1BAE40A4473D9F3F678E3BF04A1D1C2BF065CB73DD3` |
| `F09a-strain-obsoleto-cs-stirato.ghx` | 215.293 | `4B1D78CDCD9DDB5A25C91B6FB0623ED1DF973FF02A848A549926FC4B3E016169` |
| `F09b-legacy-0.3.0.ghx` | 302.635 | `6AD665E10C783FF876147E4471C62F68140D749B682A1AABA70D8E25FDE291BB` |

`F7Checks` legge `artifacts\179\input\<nome>` se il file c'è, altrimenti questa fixture; non la scrive mai. Sono ingressi della prova, non
riferimenti congelati: le prove confrontano i loro risultati con le regole scritte nel codice. Nessun dato di progetti dell'utente.
