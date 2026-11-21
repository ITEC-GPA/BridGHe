# BridGHe 0.7.1

Gli esempi correnti con il mobile nelle verifiche su **Concomitanti**. È la richiesta del 21 novembre 2026: «aggiungi i moving envelope sugli esempi. imposta concomitanti».

Il plug-in è quello della 0.7.0. Le librerie GPC sono le stesse (set ufficiale **BA** del 18 novembre 2026).

## Esempi

- **50 REV006** e **99 REV007**: la Value List «Mobile nelle verifiche» di Bridge Moving Envelope passa da Posizioni a Concomitanti.
  - La modifica è fatta nell'XML: due valori «Selected» e, nel 50, la riga MOBILE del pannello PROCEDURA. Il resto del file è byte per byte quello della 0.7.0.
  - Per la verifica finale scegliere Posizioni: i risultati sono quelli del 50 REV005 e del 99 REV006.
- Negli altri esempi con il mobile (04, 11, 28) non c'è la tabella di Bridge Moving Combinations, quindi l'inviluppo non si applica.

## Strumento per i file di progetto

`BridGHe.Smoke --example-envelope revision <in.ghx> <out.ghx> <modo> <revisione vecchia> <revisione nuova>` crea una revisione nuova di una definizione di progetto:
- inserisce Bridge Moving Envelope fra la tabella del mobile e Build BridGHe Model, con la Value List sul modo dato (0 Posizioni, 1 Inviluppo, 2 Concomitanti);
- sposta sulla revisione nuova i percorsi delle uscite Straus7 (.st7) e il nome del report (.docx);
- aggiunge un pannello di nota accanto alla Value List.

Il file di partenza è solo letto; il file di arrivo non viene mai sovrascritto.

## Prove

- Nativi di 50 REV006 e 99 REV007 nei tre modi, sul commit `cff166b`:
  - le righe sono identiche alla 0.7.0; cambia solo l'impronta del file della definizione;
  - riferimenti in `tests/BridGHe.Smoke/Fixtures/native-gate-070/references-071.json`, righe complete in `P2B\results\t071-native`.
- Gate completo confrontato con le catture della 0.7.0 (scansione, payload, corpus e replay delle quattro corsie): tutto uguale.
  - Le 107 regressioni passano.
  - Con Concomitanti nessuna voce del replay cambia, quindi le catture della 0.7.0 restano quelle di riferimento.
- Controllo di allineamento delle 49 definizioni del catalogo (porte e collegamenti contro la tavolozza della 0.7.1, componenti superati o nascosti, Detail Check collegati direttamente al Super Element Check):
  - tutte hanno le porte della 0.7.1 e nessun componente superato;
  - il 48 tiene il Detail Check sulle stazioni del Super Element Check, perché è un tratto di 2,5 m senza appoggi né conci: l'indicazione della 0.7.0 lì è solo informativa.
