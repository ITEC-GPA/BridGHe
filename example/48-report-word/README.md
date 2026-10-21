# 48 Report Word da template

Aprire [48-report-word.ghx](48-report-word.ghx). Il file estende il flusso dell'esempio 47 con **Bridge Report**, una checklist dei capitoli e i tre grafici di pioli e irrigiditori.

1. Premere Run su **Build BridGHe Model**.
2. Premere Run su **Super Element Check**.
3. Scegliere gli argomenti nella checklist di Bridge Report.
4. Impostare un nuovo File, oppure chiudere il documento precedente in Word e attivare Overwrite.
5. Premere **Generate**.

Il modello, i risultati dei dettagli e gli SVG sono già collegati. I percorsi del template e del report sono relativi alla definizione salvata. Non serve una nuova analisi Straus: il file conserva l'estratto dei risultati archiviati dell'esempio 19, beam 36 fra X=27.5 e 30 m. La combinazione con fattori unitari e la reazione assegnata di 900 kN sono dimostrative, non una verifica completa o una combinazione normativa SLU.

## File pronti

- [Template_BridGHe.docx](Template_BridGHe.docx): template modificabile con segnaposto separati per capitolo.
- [Report_esempio.docx](Report_esempio.docx): report con modello, sezioni, fasi, verifiche governanti, schede e grafici.
- [Report_sintetico.docx](Report_sintetico.docx): stesso insieme di risultati con la checklist vuota; rimane il riepilogo della copertura.

Per personalizzare l'ordine spostare i paragrafi `{{BG.Model}}`, `{{BG.Sections}}` ecc. nel template. Per un proprio documento Word è sufficiente aggiungere un paragrafo `{{BG.Report}}`; il generatore conserva copertina, loghi, intestazioni e stili. La selezione dei capitoli non nasconde le lacune del riepilogo. [Guida completa](../../docs/REPORT.md).

## Rigenerazione e test

```powershell
dotnet build tests/BridGHe.Smoke -c Release
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression bridge-report
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression bridge-report-component
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-report-example
```

Il template si rigenera con `tools/create_report_template.py` usando Python con python-docx. Questo sovrascrive il template dimostrativo: conservare separatamente le personalizzazioni dello studio.

I test controllano WR e azioni invariati, lacune e parità, provenienza, selezione dei capitoli, segnaposto divisi tra run, stili e intestazioni conservati, conversione DOTX, archivio incorporato, protezione di template/file aperti e assenza di scritture automatiche alla riapertura GH.
