# Provenienza della fixture `p2d-payloads` (P2CA, 13/11/2026)

Payload dei controlli Development e Construction della regressione `p2d`, **scritti una volta sola** dal writer FEM di BridGHe (`ModelArchive.Write`) mentre esisteva. Ha la forma di `legacy-payloads`. Decisione dell'utente del 13/11 verso le 07:00 («Sì, congelale»), sentita dal coordinatore. Non va mai rigenerata.

- **Commit del generatore:** BridGHe `d922b94` (ramo `claude/f6-bridghe-p2r`); Model `fefb47bd`, Checker `9e513417`; build Release `-p:GpcPilot=true`.
- **Generatore:** `P2dChecks.Freezer` di quel commit, lanciato così:
  `BRIDGHE_P2D_FIXTURES=write:<questa cartella> dotnet BridGHe.Smoke.dll --regression p2d`.
  - La cartella non doveva esistere.
  - L'indice viene scritto solo con tutti i controlli verdi.
  - Il generatore è stato tolto nel commit successivo, insieme agli input FEM.
- **Contenuto** (9 file e `index.tsv`: file, unità, byte, SHA-256, esito):
  - **Development.** Il modello FEM del caso (`ExampleRemake04.WithEndNodes` sulla sorgente dei test delle utilità: 4 nodi e 2 travi, N-mm):
    - `p2d-node.bgha1`, il nodo 1;
    - `p2d-beam-1.bgha1`, la seconda trave;
    - `p2d-material-steel.bgha1`, S355 di tipo Steel;
    - `p2d-model.bgha1`, il modello;
    - i datum BGHGPC1 delle conversioni GPC usate dal controllo: `p2d-beam-0-datum.data/.records` (prima trave) e `p2d-model-datum.data/.records` (modello).
  - **Construction.** `p2d-construction-model.bgha1`: lo stesso modello, con la prima trave nei gruppi A e B e le altre in B.
- **Lettura:** `Gpc/P2dPayloads.cs`, senza classi FEM; controlla dimensione e SHA-256 dell'indice.
