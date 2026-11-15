# Provenienza della fixture `draft-merge` (P2CA, 13/11/2026)

Oracolo congelato della regressione `gpc-draft-merge`, **scritto una volta sola** dalla merge FEM di prima (`new XModel(first).Merge(other)`, la merge di `ModelHelper.BuildModel` di BridGHe 0.3.0), mentre i tipi FEM esistevano. Decisione dell'utente del 13/11 verso le 07:00 («Sì, congelale»), sentita dal coordinatore. Non va mai rigenerata.

- **Commit del generatore:** BridGHe `d922b94` (ramo `claude/f6-bridghe-p2r`); Model `fefb47bd`, Checker `9e513417`; build Release `-p:GpcPilot=true`.
- **Generatore:** `GpcDraftMergeChecks.Freezer` di quel commit, lanciato così:
  `BRIDGHE_DRAFT_MERGE_FIXTURE=write:<questa cartella> dotnet BridGHe.Smoke.dll --regression gpc-draft-merge`.
  - La cartella non doveva esistere.
  - Un caso veniva scritto solo se la merge dei draft riletti dai loro byte dava lo stesso testo della merge dei draft in memoria.
  - Il generatore è stato tolto nel commit successivo, insieme agli input FEM.
- **Contenuto:**
  - 17 confronti, di cui 5 con l'errore della merge;
  - 34 draft d'ingresso in BGHGPC1 (`GpcDraftArchive.Write`): `drafts/<id>.data` e `<id>.records`. L'id sono le prime 16 cifre esadecimali dello SHA-256 dei byte. Ogni conversione FEM crea identità nuove, quindi nessun draft si ripete;
  - i testi attesi, cioè il draft della merge FEM risolto: `expected/NN.txt`, UTF-8;
  - `index.tsv`: etichetta, tipo, id dei due draft, testo atteso, SHA-256 del testo, errore;
  - `drafts.tsv`: id, tipo, byte e SHA-256 di data e records.
- **Lettura:** `GpcDraftMergeChecks.Regression`, senza classi FEM. Controlla dimensioni e SHA-256, unisce i due draft letti con `LegacyBuildModel.Merge`, risolve con `LegacyAssembly` e confronta il testo.
