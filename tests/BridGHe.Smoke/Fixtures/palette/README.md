# Frozen palette table of BridGHe 0.4.0 (proposal, 14/11/2026)

Lane P2CI, user decision 3 of 14/11. Files: `components.json` (228 GUIDs: 220 components, 8 stand-alone parameters) and
`ports.json` (1,880 ports). They become `tests/BridGHe.Smoke/Fixtures/palette/` only after the user's OK.

- Source: the static extraction of ChatGPT task 56 at 59e9def (`parita-componenti-040.csv`), made into JSON by task 78 (patch 78,
  written against 9350aeb), checked by `--regression palette-contract` on branch `claude/f6-bridghe-p2ci` (base 5fd9e38).
- 46 rows corrected by hand from the source, each with its file:line in the `Correction` field and in `correzioni.tsv`: the runtime
  and the source agree, the static extraction did not (no registration changed between 59e9def and 5fd9e38). Exposure: no
  correction (the default `primary` is confirmed).
- The check never writes these files and pins them by SHA-256 (UTF-8, LF):
  components.json 29B615F73989770A8419CBA84D7D4B1EA5ED852EF6E5A6456CEB22240A8230F3,
  ports.json AFC08B66A7CABA2F7817DEC7380C0256CE2A3D783726B3B7944957D5CED48BDB.
- Update procedure: the failing run names the rows; each changed row is explained and approved by the user; only those rows are
  edited by hand; the two SHA-256 in `PaletteContractChecks.cs` change in the same commit, whose message names the decision.
- Outside the table: the NamedValues of the menus, the preview state (GH_Hidden), the nicknames that Custom Material sets when it
  solves (the Units menu), and user customisations saved in a document.
