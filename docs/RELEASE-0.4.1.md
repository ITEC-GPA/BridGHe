# BridGHe 0.4.1 — candidato nei cloni

Stato: preparazione della 0.4.1, non un rilascio pubblicato. La qualificazione del nuovo set GPC e la prova dell'utente precedono la distribuzione.

## Sezioni

I cinque componenti Straus duplicati di Solid Circle, Hollow Circle, Solid Rect, Hollow Rect e I sono obsoleti e nascosti. GUID, porte e lettura dei vecchi documenti restano disponibili; i domini estesi non coperti conservano i componenti Straus dedicati.

T, C e Angle dei builder GPC calcolano le proprietà con Model. Converter 2.23.0.1 del candidato AX esporta i domini standard nelle forme native Straus7 8, 5 e 9 mantenendo autorevoli le proprietà GPC. Sono conservate le convenzioni di mirror/gap e la rotazione legacy del doppio Angle. I valori geometrici mostrati comprendono ora la torsione GPC e le due parti del doppio C.

**Opzione A preparata, in attesa della decisione dell'utente.** J e centro di taglio di T/C/L restano quelli GPC a parete sottile; non sono equivalenti in generale al calcolatore Straus7 usato nella 0.4.0. Nei casi misurati J GPC/Straus vale 1,072–1,076 per le doppie C e 1,081–1,084 per le doppie L; rispetto al doppio della J singola Straus i rapporti sono invece 1,018 e 1,023. Nelle doppie L il centro GPC è a 40,116 mm dal baricentro, mentre Straus restituisce zero: la convenzione osservata è dichiarata e il vettore GPC è verificato analiticamente. Nei tre profili tozzi T-48x40, C-40x30 e L-40x30 J è maggiore del 5,8–16,5%, con rapporti dei centri di taglio fino a 1,96. I componenti avvisano, senza cambiare i valori, quando max(tf/B, tw/H) > 0,30; B e H sono le dimensioni complessive di una singola parte, anche per le coppie. È una soglia di avviso ricavata dai casi misurati, non una garanzia di accuratezza sotto soglia: il C-300x80, con rapporto 0,125, mostra comunque 4,09 mm (1,5%) di scarto del centro di taglio. Le 23 differenze note sono classificate nella prova; ogni variazione dei valori numerici registrati produce ancora un FAIL. Le altre soglie del confronto fisico restano invariate.

Questi cambi richiedono il nuovo set coerente dopo AW. Il candidato del compito 190 è provato con consenso esplicito al candidato; AW non soddisfa il requisito del Converter. Le differenze rispetto alle catture esistenti restano da approvare: non si promette equivalenza numerica generale con Straus7. I confronti nativi di torsione, taglio e centri sono distinti dalle prove analitiche gestite.

## Correzioni e diagnostica

Le code vuote degli avvisi di lettura evitano la scansione dei parametri; InstanceGuid duplicati non interrompono la soluzione. I carichi puntuali usano la stessa classificazione Local/Global dei distribuiti e rifiutano esplicitamente frame diversi non supportati.

Rhino riceve una riga informativa con percorso e SHA-256 della MathNet effettivamente caricata; il calcolo dell'hash è differito fuori dall'evento di caricamento. La diagnostica non modifica la politica di caricamento delle librerie.

Le prove dei digest dei report normalizzano soltanto il proprio prefisso TEMP. La pulizia interna conserva i contratti funzionali; le differenze dell'audit testuale dovute ai nomi privati restano documentate, senza rigenerare gli attesi.

## Report Word e completamento

La versione destinata al rilascio comprende il report con indice e 36 allegati scelto dall'utente. L'implementazione è affidata al compito 195: non è inclusa nella consegna del ramo del compito 192. Il collaudo finale comprenderà nativi 50/99 e prova dell'utente sul candidato completo.

Conservare gli originali prima di ricalcolare e salvare. Nessuna modifica ai formati degli archivi è introdotta dalle integrazioni del 192. Distribuire soltanto pacchetti completi con il manifest del set approvato; questo clone non costituisce un pacchetto installabile.
