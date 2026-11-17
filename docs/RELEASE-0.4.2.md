# BridGHe 0.4.2 (nota provvisoria)

Stato: **provvisoria, prima parte**. Non è un rilascio pubblicato. Le librerie GPC sono ancora quelle del set ufficiale AX del 16 novembre 2026 (`gpc-release-20261116-ax`, `candidate=false`), le stesse della 0.4.1: GPCModel 5.51.0.2, GPCModelData 0.3.0.32, GPC.Model.Persistence 3.15.0.4, GPC.Archive 1.2.0.5, GPC.Converter 2.23.0.1, GPCChecker.Concrete 0.37.0.3, GPCChecker.Steel 2.134.0.2, GPCChecker.CompositeBridge 1.31.0.3, GPCUtilities 3.0.0.0, GPCGeometry 2.3.0.0 e DelaunayMesh 2.0.0.14. La seconda parte (J e centro di taglio di T, C e Angle con il metodo numerico, «opzione B») arriverà con un set GPC nuovo e cambierà questa nota.

## Report Word diviso in indice e allegati

Riguarda i report con più di 1000 risultati.

- **Copia di recupero dell'indice.** Se la sostituzione di un indice esistente si interrompe e anche il ripristino fallisce (rete, filtri del file system), la copia dell'indice precedente resta accanto al report, nel percorso indicato dal messaggio. Prima finiva nella cartella temporanea, che veniva cancellata.
- **Nuovi tentativi.** Le rinomine delle cartelle si ritentano fino a 5 volte, a 0,3 s di distanza, quando un antivirus, l'indicizzatore o una cartella sincronizzata tengono ancora aperto un file. Se la cartella temporanea non si riesce a cancellare, il messaggio d'errore resta quello vero e ne indica il nome; a report completato diventa un avviso.
- **Percorsi lunghi.** Il nome del report compare due volte nel percorso degli allegati (`nome_allegati\nome_A01.docx`). Oltre 218 caratteri Bridge Report dà un avviso con il file e la lunghezza: Word ed Excel possono non aprire i file né seguire i collegamenti dell'indice. I nomi dei file restano quelli della 0.4.1.
- **File in sola lettura.** Un indice o un allegato in sola lettura (copiato da un archivio o da un CD, o con l'attributo messo da Esplora risorse) viene segnalato come tale, invece di chiedere di chiudere Word.

## Sezioni aperte

L'avviso sui profili tozzi («Thick open section: thin-wall torsion and shear centre may be inaccurate.») compare anche quando una sezione T, C o Angle, singola o doppia, nasce da un testo, per esempio `L40x30x14x12` in un Panel collegato a Beam Property o a Section Geometric Property. La regola è quella della 0.4.1: max(tf/B, tw/H) > 0,30, con B e H di una singola parte. I valori delle sezioni non cambiano.

## Prove

Le prove delle sezioni aperte senza Straus7 (sezioni uniche, sezioni aperte, oracolo, replay e avvisi) fanno ora parte della regressione completa.

Conservare gli originali prima di ricalcolare e salvare. Il formato degli archivi non cambia rispetto alla 0.4.1. Distribuire soltanto pacchetti completi, con il manifest `gpc-release-set.json` del set AX; usare ReBear e Arachne dello stesso rilascio.
