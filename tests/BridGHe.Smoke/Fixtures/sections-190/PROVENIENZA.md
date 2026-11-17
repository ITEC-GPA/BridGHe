# Documenti di base del compito 190

`old-five-sections.ghx` e `old-five-sections.gh` sono i due documenti che `--sections-190-before` ha scritto il 15/11/2026 alle 15:13 sulla base del compito 190 (prima di `7079d96`, che ha reso obsoleti e nascosti i cinque componenti Straus duplicati di Solid Circle, Hollow Circle, Solid Rect, Hollow Rect e I). Ognuno contiene i cinque componenti vecchi bloccati, con numeri salvati 100, 101, … e un filo in ingresso e in uscita.

Origine: `CX\BridGHe-190\artifacts\190\` (il clone del compito 190), copiati senza modifiche il 17/11/2026 per la 0.4.2 (D6 della revisione finale della 0.4.1): `regression all` esegue ora `sections-190`, e un checkout nuovo non ha la propria cartella `artifacts\190`.

| File | Byte | SHA-256 |
|---|---:|---|
| `old-five-sections.ghx` | 84.605 | `0EB5BE147E5D51F47399BE658FBD3CDAD950E7665688B26098C0F1D55F251C7B` |
| `old-five-sections.gh` | 2.404 | `2AE7EBACABC9A0AD1615A04444C05A8050568742582280E8C86BC3E0625D4C37` |

`UniqueSectionChecks.Archives` li legge solo se `artifacts\190` non ha i suoi (scritti da `--sections-190-before`); non li scrive mai. Sono ingressi della prova, non riferimenti congelati: la prova controlla che i componenti vecchi si riaprano con classe, blocco, porte, numeri e fili.
