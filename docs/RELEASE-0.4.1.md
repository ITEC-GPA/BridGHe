# BridGHe 0.4.1

Le librerie GPC sono quelle del set ufficiale del 16 novembre 2026 (AX, `gpc-release-20261116-ax`, `candidate=false`), comune con ReBear 0.4.3 e Arachne 0.4.1: GPCModel 5.51.0.2, GPCModelData 0.3.0.32, GPC.Model.Persistence 3.15.0.4, GPC.Archive 1.2.0.5, GPC.Converter 2.23.0.1, GPCChecker.Concrete 0.37.0.3, GPCChecker.Steel 2.134.0.2, GPCChecker.CompositeBridge 1.31.0.3, GPCUtilities 3.0.0.0, GPCGeometry 2.3.0.0 e DelaunayMesh 2.0.0.14. Rispetto ad AW cambia il comportamento solo GPC.Converter (esportazione Straus7 di T, C e Angle, sotto); le altre librerie sono ricompilate sugli stessi sorgenti.

## Sezioni

I cinque componenti Straus duplicati di Solid Circle, Hollow Circle, Solid Rect, Hollow Rect e I sono obsoleti e nascosti dalla palette. GUID, porte e lettura dei vecchi documenti restano disponibili; i domini estesi non coperti conservano i componenti Straus dedicati.

T, C e Angle dei builder GPC calcolano le proprietà con Model. GPC.Converter 2.23.0.1 esporta i domini standard nelle forme native Straus7 8, 5 e 9, mantenendo autorevoli le proprietà GPC. Sono conservate le convenzioni di mirror/gap e la rotazione legacy del doppio Angle. I valori geometrici mostrati comprendono ora la torsione GPC e le due parti del doppio C.

**J e centro di taglio di T, C e Angle (opzione A, scelta il 16/11/2026).** J e centro di taglio restano quelli GPC a parete sottile; non sono equivalenti in generale al calcolatore Straus7 usato nella 0.4.0.
- Doppie C: J GPC/Straus vale 1,072–1,076; doppie L: 1,081–1,084. Rispetto al doppio della J singola Straus i rapporti sono 1,018 e 1,023.
- Doppie L: il centro GPC è a 40,116 mm dal baricentro, mentre Straus restituisce zero. È la convenzione osservata per le coppie speculari; il vettore GPC è verificato analiticamente.
- Profili tozzi T-48x40, C-40x30 e L-40x30: J maggiore del 5,8–16,5 %, rapporti dei centri di taglio fino a 1,96.
- I componenti T, C e Angle danno un avviso, senza cambiare i valori, quando max(tf/B, tw/H) > 0,30. B e H sono le dimensioni complessive di una singola parte, anche per le coppie. Testo: «Thick open section: thin-wall torsion and shear centre may be inaccurate.» È una soglia di avviso ricavata dai casi misurati, non una garanzia di accuratezza sotto soglia: il C-300x80, con rapporto 0,125, mostra comunque 4,09 mm (1,5 %) di scarto del centro di taglio.
- Le 23 differenze misurate sono dichiarate una per una nella prova nativa delle sezioni (4974/4974 PASS sul set AX); ogni variazione dei valori registrati produce un FAIL. Le altre soglie del confronto fisico restano invariate.

Rispetto alle catture della 0.4.0 cambiano soltanto i dieci campi della palette (Exposure e Obsolete dei cinque duplicati) e due digest delle proprietà (Section Geometric Property, double channel e double angle), approvati dall'utente il 16/11/2026. Non si promette equivalenza numerica generale con Straus7 per J e centro di taglio delle sezioni aperte.

## Report Word: indice e allegati

Con più di **1000 risultati** Bridge Report divide da solo il documento: un indice, un allegato per combinazione (36 nell'esempio 50 REV003) e l'archivio numerico `…_archivio.zip` nella cartella degli allegati. Fino a 1000 risultati il documento resta unico, come prima. Non ci sono ingressi nuovi né una scelta manuale del documento unico; porte e GUID sono invariati. Sull'esempio 50 la generazione passa da circa 13 minuti a meno di un minuto e Word apre l'indice in pochi secondi. Il contenuto è lo stesso (34.128 risultati). Dettagli in [REPORT.md](REPORT.md#report-grandi-indice-e-allegati).

## Correzioni e diagnostica

Le code vuote degli avvisi di lettura evitano la scansione dei parametri; InstanceGuid duplicati non interrompono la soluzione. I carichi puntuali usano la stessa classificazione Local/Global dei distribuiti e rifiutano esplicitamente frame diversi non supportati.

Rhino riceve una riga informativa con percorso e SHA-256 della MathNet effettivamente caricata; il calcolo dell'hash è differito fuori dall'evento di caricamento. La diagnostica non modifica la politica di caricamento delle librerie.

Le prove dei digest dei report normalizzano soltanto il proprio prefisso TEMP. La pulizia interna conserva i contratti funzionali.

## Verifiche e installazione

Sul set AX, senza flag per i candidati: regressioni 98 gruppi PASS (gli unici due FAIL sono le differenze approvate sopra), F7 198/198 e 365/365, sezioni 203/203 e 11.082/11.082. Nativi Straus7: sezioni 4974/4974; esempi 50 e 99 con risultati uguali riga per riga a quelli qualificati (66.960 e 2.832 righe) e report diviso controllato.

Conservare gli originali prima di ricalcolare e salvare. Il formato degli archivi non cambia rispetto alla 0.4.0. Distribuire soltanto pacchetti completi, con il manifest `gpc-release-set.json` del set AX; usare ReBear e Arachne dello stesso rilascio.
