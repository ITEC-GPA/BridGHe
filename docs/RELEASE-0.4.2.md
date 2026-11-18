# BridGHe 0.4.2 (nota provvisoria)

Stato: **provvisoria, parti 1 e 2**. Non è un rilascio pubblicato. Le librerie GPC sono quelle del set ufficiale **BA** del 18 novembre 2026 (`gpc-release-20261118-ba`, `candidate=false`), comune con ReBear 0.4.4 e Arachne 0.5.1. Gli esiti delle prove su questo set si aggiungono prima della pubblicazione.

## Librerie GPC

GPCModel 5.54.0.0, GPCModelData 0.4.0.0, GPC.Model.Persistence 3.17.0.1, GPC.Archive 1.2.0.10, GPC.Converter 2.27.0.1, GPCChecker.Concrete 0.39.0.2, GPCChecker.Steel 2.136.0.2, GPCChecker.CompositeBridge 1.31.0.10; GPCUtilities 3.0.0.0, GPCGeometry 2.3.0.0 e DelaunayMesh 2.0.0.14 sono identiche alla 0.4.1 (set AX). Model `55c15ed5` e Checker `5bdb88dd` su origin/master: l'opzione B delle sezioni aperte sopra il set AZ.

Per BridGHe cambiano:
- **J e centro di taglio delle sezioni aperte** (sotto);
- **freedom case** dell'esportazione Straus7 (GPC.Converter 2.25 e 2.27): vincoli, molle e casi di carico restano legati al loro freedom case. I modelli di BridGHe ne hanno uno solo, quindi i file Straus7 scritti non cambiano;
- le altre novità dei set AY e AZ (equilibrio delle piastre piane, verifiche nuove di Concrete e Steel) non toccano i componenti di BridGHe.

## Sezioni aperte: J e centro di taglio numerici (opzione B)

T, C e Angle, singoli e doppi, prendono J e centro di taglio dal calcolo numerico di Model (elementi finiti di Saint-Venant sul contorno vero), non più dalle formule a parete sottile della 0.4.1 («opzione A»). Area, inerzie, assi, aree di taglio e Iw non cambiano.

- **Singoli** (i casi della qualifica delle sezioni): J diminuisce dallo 0,6 al 2,4 % per i profili normali, fino al 13,7 % per quelli tozzi (T 48x40 con ali da 18 mm); il centro di taglio si sposta fino a 11 mm (C 40x30). Contro il calcolatore Straus7 usato dalla 0.4.0, J resta entro lo 0,5 % e il centro di taglio entro 0,08 mm.
- **Coppie 2C e 2L** (parti collegate solo per punti): J è la somma delle due parti e il centro di taglio della 2L sta sull'asse di simmetria, all'altezza di quello della L (per la 2C è il baricentro). Rispetto al doppio della J della parte calcolata da Straus7 i rapporti sono 1,0028 (2C) e 1,0018 (2L); il centro della 2L è a 0,0075 mm da quello della L di Straus7. Straus7 dà alla coppia una J del 5–6 % più bassa e il centro nel baricentro: sono le 15 differenze dichiarate una per una nella prova nativa delle sezioni.
- **Avviso dei profili tozzi tolto.** «Thick open section: thin-wall torsion and shear centre may be inaccurate» non compare più, né nei componenti T, C e Angle né per una sezione scritta come testo in Beam Property o Section Geometric Property: i valori ora sono quelli giusti anche per i profili tozzi. Porte e GUID non cambiano.
- **Section Geometric Property** mostra le J nuove; per le coppie cambiano due digest delle catture delle proprietà (double channel e double angle), registrati solo dopo l'approvazione.
- **Risultati salvati con la 0.4.1.** Un archivio di risultati (`.bghr` accanto al `.st7`, o risultati internalizzati nel documento) conserva la J con cui è stato calcolato. Se un'asta principale (Girder) ha una sezione T, C o Angle, Bridge State Override confronta quella J con la J nuova della definizione e chiede di ricalcolare («reference geometry or steel stiffness changed. Recalculate model 0.»). Basta rilanciare l'analisi; i risultati salvati con la 0.4.2 si rileggono uguali. Gli esempi distribuiti non hanno aste T, C o Angle.

## Report Word diviso in indice e allegati

Riguarda i report con più di 1000 risultati.

- **Copia di recupero dell'indice.** Se la sostituzione di un indice esistente si interrompe e anche il ripristino fallisce (rete, filtri del file system), la copia dell'indice precedente resta accanto al report, nel percorso indicato dal messaggio. Prima finiva nella cartella temporanea, che veniva cancellata.
- **Nuovi tentativi.** Le rinomine delle cartelle si ritentano fino a 5 volte, a 0,3 s di distanza, quando un antivirus, l'indicizzatore o una cartella sincronizzata tengono ancora aperto un file. Se la cartella temporanea non si riesce a cancellare, il messaggio d'errore resta quello vero e ne indica il nome; a report completato diventa un avviso.
- **Percorsi lunghi.** Il nome del report compare due volte nel percorso degli allegati (`nome_allegati\nome_A01.docx`). Oltre 218 caratteri Bridge Report dà un avviso con il file e la lunghezza: Word ed Excel possono non aprire i file né seguire i collegamenti dell'indice. I nomi dei file restano quelli della 0.4.1.
- **File in sola lettura.** Un indice o un allegato in sola lettura (copiato da un archivio o da un CD, o con l'attributo messo da Esplora risorse) viene segnalato come tale, invece di chiedere di chiudere Word.
- **Descrizioni delle porte.** Results, Overwrite e Report ID di Bridge Report descrivono anche il report diviso; i contratti delle porte sono aggiornati.

## Esempi 50 e 99

Le definizioni correnti sono ora **50 REV004** e **99 REV005**, con i carichi delle fasi ripresi dallo script di riferimento del ponte misto: peso proprio dell'acciaio con il fattore 1,15 delle connessioni, getto sul solo acciaio, permanenti portati (pavimentazione, cordoli, guard-rail, parapetti e finiture), distribuiti e tandem dello schema 1, frenatura, centrifuga (0,01 kN/m per trave), vento e temperatura uniforme. Il ritiro resta nel suo modello. Nel 99 c'è una fase nuova con il tandem mobile. Le revisioni precedenti restano come definizioni aggiuntive. I limiti rispetto allo script (distribuiti statici, una sola disposizione delle corsie, niente gradiente termico, sisma, fatica e folla) sono scritti nei README delle due revisioni.

Nel 50 REV004 gli irrigiditori d'appoggio sono quelli del REV003: con i carichi nuovi il rapporto massimo delle verifiche locali è 3,03 sulle saldature alle flange e 98 contatti piatti/flangia risultano non verificati. È un esito dell'esempio, scritto nel suo README.

## Prove

- Le prove delle sezioni aperte senza Straus7 (sezioni uniche, sezioni aperte, oracolo, replay e avvisi) fanno parte della regressione completa.
- La prova nativa delle sezioni confronta i singoli con le soglie del 198 senza dichiarazioni e le coppie con la parte singola (J con il doppio della J di Straus7, centro di taglio con quello della parte di Straus7); restano dichiarate le 15 differenze delle coppie contro la coppia di Straus7. I valori registrati della prova sono quelli dell'opzione B.
- Le prove native degli esempi 50 e 99 calcolano le revisioni nuove e le confrontano con i loro riferimenti registrati (righe di verifica e impronte).
- La tabella della palette registra i 10 campi della 0.4.1 (Exposure e Obsolete dei cinque componenti Straus duplicati) e le descrizioni nuove di Bridge Report.
- Gli ingressi delle prove F7 (F05b, F09a, F09b) sono fixture versionate: un checkout nuovo non dipende più da file copiati a mano.

Conservare gli originali prima di ricalcolare e salvare. Il formato degli archivi non cambia rispetto alla 0.4.1. Distribuire soltanto pacchetti completi, con il manifest `gpc-release-set.json` del set BA; usare ReBear 0.4.4 e Arachne 0.5.1, dello stesso set. Arachne 0.5.0 gira solo con Rhino 8 su .NET 8 (non con l'opzione .NET Framework di Rhino); BridGHe e ReBear sono già su .NET 8.
