# BridGHe 0.6.1

Calcolo più veloce del Build Bridge Model con molte posizioni del mobile. I risultati sono identici alla 0.6.0, bit per bit.

Le librerie GPC sono le stesse della 0.6.0 (set ufficiale **BA** del 18 novembre 2026).

## Perché

Ogni posizione del mobile è un'azione. Per ogni azione e per ogni asta, Build Bridge Model leggeva le forze di **tutti** i casi e le combinazioni del modello mobile, ricombinando al volo quelle non salvate, e poi teneva solo quelle del caso dell'azione. Il costo cresceva quindi col quadrato del numero di posizioni.

| Ponte sul Pescara, 114 posizioni, definizione risolta | 0.6.0 | 0.6.1 |
|---|---:|---:|
| Build Bridge Model | 507 s | 57 s |
| Definizione | 758 s | 328 s |
| Righe dei risultati | 183.022 | 183.022, identiche |

## Cosa cambia nel codice

- `GpcResultRows.Beam` ha una variante con l'etichetta del caso: salta gli altri casi prima di leggerne o combinarne le forze. Le righe del caso sono quelle di prima, nello stesso ordine.
- Build Bridge Model trova una volta per azione i casi che contribuiscono ai carichi concentrati, invece di farlo per ogni asta.
- Nell'unione delle stazioni, i campioni di ogni caso si raggruppano una volta sola.
- Anche la classificazione dei momenti delle aste legge solo il caso richiesto.

Nessun componente, ingresso o uscita cambia; le definizioni della 0.6.0 si aprono uguali.

## Prove

- Pescara a 114 posizioni: le righe dei risultati e le impronte di ogni componente sono identiche alla 0.6.0.
- Gate ridotto: build, audit, core-only, regressione completa e GPC-only models con le catture della 0.6.0.
- Nativi 50 REV005 e 99 REV006: righe uguali ai riferimenti della 0.6.0 (`Fixtures/native-gate-060`).
