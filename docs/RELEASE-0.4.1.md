# BridGHe 0.4.1 — candidato nei cloni

Stato: preparazione della 0.4.1, non un rilascio pubblicato. La qualificazione del nuovo set GPC e la prova dell'utente precedono la distribuzione.

## Sezioni

I cinque componenti Straus duplicati di Solid Circle, Hollow Circle, Solid Rect, Hollow Rect e I sono obsoleti e nascosti. GUID, porte e lettura dei vecchi documenti restano disponibili; i domini estesi non coperti conservano i componenti Straus dedicati.

T, C e Angle dei builder GPC calcolano le proprietà con Model. Converter 2.23.0.0 esporta i domini standard nelle forme native Straus7 8, 5 e 9 mantenendo autorevoli le proprietà GPC. Sono conservate le convenzioni di mirror/gap e la rotazione legacy del doppio Angle. I valori geometrici mostrati comprendono ora la torsione GPC e le due parti del doppio C.

Questi cambi richiedono il nuovo set coerente dopo AW. Il candidato del compito 190 è provato con consenso esplicito al candidato; AW non soddisfa il requisito del Converter. Le differenze rispetto alle catture esistenti restano da approvare: non si promette equivalenza numerica generale con Straus7. I confronti nativi di torsione, taglio e centri sono distinti dalle prove analitiche gestite.

## Correzioni e diagnostica

Le code vuote degli avvisi di lettura evitano la scansione dei parametri; InstanceGuid duplicati non interrompono la soluzione. I carichi puntuali usano la stessa classificazione Local/Global dei distribuiti e rifiutano esplicitamente frame diversi non supportati.

Rhino riceve una riga informativa con percorso e SHA-256 della MathNet effettivamente caricata; il calcolo dell'hash è differito fuori dall'evento di caricamento. La diagnostica non modifica la politica di caricamento delle librerie.

Le prove dei digest dei report normalizzano soltanto il proprio prefisso TEMP. La pulizia interna conserva i contratti funzionali; le differenze dell'audit testuale dovute ai nomi privati restano documentate, senza rigenerare gli attesi.

## Report Word e completamento

La versione destinata al rilascio comprende il report con indice e 36 allegati scelto dall'utente. L'implementazione è affidata al compito 195: non è inclusa nella consegna del ramo del compito 192. Il collaudo finale comprenderà nativi 50/99 e prova dell'utente sul candidato completo.

Conservare gli originali prima di ricalcolare e salvare. Nessuna modifica ai formati degli archivi è introdotta dalle integrazioni del 192. Distribuire soltanto pacchetti completi con il manifest del set approvato; questo clone non costituisce un pacchetto installabile.
