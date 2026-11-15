# Provenienza della fixture `lane3d-payloads` (P2CC, 13/11/2026)

Payload BGHA1 dei valori FEM costruiti in memoria dai casi di `GpcOnlyReplay`, **scritti una volta sola** dal writer FEM di BridGHe (`ModelArchive.Write`) mentre esisteva. Riguardano Lane 3d (Bridge Phase Model e Bridge Construction Sequence) e gli audit sugli esempi 02 e 03. Decisione dell'utente del 13/11 alle 12:35 («Congela»), sentita dal coordinatore. Non va mai rigenerata.

- **Commit del generatore:** BridGHe `38f7e05` (ramo `claude/f6-bridghe-p2r`); Model `fefb47bd`, Checker `9e513417`; build Release `-p:GpcPilot=true`.
- **Generatore:** `GpcOnlyReplay.P2ccFreezer` di quel commit, lanciato così:
  `BRIDGHE_P2CC_FIXTURES=write:<questa cartella> BRIDGHE_GPC_ONLY_TYPES=BridgePhaseModelComponent,BridgeConstructionSequenceComponent,BridgeModelAuditComponent BRIDGHE_GPC_ONLY_FILES=no-such-definition dotnet BridGHe.Smoke.dll --regression gpc-only-models`.
  - La cartella non doveva esistere.
  - Un valore veniva scritto solo se il suo payload, riletto per valore (`GpcLegacyPayloads`, i tipi GH di 0.4.0), dava lo stesso testo del tipo GH del valore FEM, con i GUID sostituiti dai loro ordinali.
  - Il generatore è stato tolto nel commit successivo, insieme agli input FEM.
- **Contenuto** (15 payload, `index.tsv`: file, unità, byte, SHA-256, esito):
  - **modelli:**
    - `p2cc-m2`, l'esempio 02 del catalogo FEM;
    - `p2cc-m3`, l'esempio 03;
    - `p2cc-named`, l'esempio 02 con i BridGHe ID B1…B4;
    - `p2cc-linked`, l'esempio 02 con un collegamento rigido;
    - `p2cc-mixed`, `p2cc-two-groups`, `p2cc-ungrouped`: tre travi e una piastra nei gruppi A, A\Child e B, con il caso registrato Q;
    - `p2cc-bridge`, il ponte semplice di Bridge Member Assignment;
  - **proprietà di trave:**
    - `p2cc-stiff` e `p2cc-stiff-knm`, con E × 1,25;
    - `p2cc-composite`, la proprietà composita;
    - `p2cc-same`, con E × 1,25;
  - **travi A09:** `p2cc-a09-B1`, `-B2`, `-B3`.
- **Escluso:** i dati di trave in kN-m dei test (`ImportDump.BeamData`) hanno già il loro payload congelato, `beams-Knm` di `legacy-payloads`. Riletto per valore non dà lo stesso testo: lo stato di sezione della trave G14 non risulta «stale» come lo segnava la conversione FEM. Non è stato riscritto. I due casi che lo usano sono ritirati per nome.
- **Lettura:** `Gpc/Lane3dPayloads.cs`, senza classi FEM; controlla dimensione e SHA-256 dell'indice.
