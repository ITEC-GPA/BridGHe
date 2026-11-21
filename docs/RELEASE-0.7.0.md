# BridGHe 0.7.0

Il mobile nelle verifiche (posizioni, inviluppo o concomitanti) e uscite più leggere. Sono le decisioni del 21 novembre 2026.

Le librerie GPC sono le stesse della 0.6.1 (set ufficiale **BA** del 18 novembre 2026).

## Bridge Moving Envelope (componente nuovo)

Va fra Bridge Moving Combinations (Table) e Build BridGHe Model (Combinations) e sceglie come entrano nelle verifiche le posizioni del mobile.

| Mode | Che cosa fa | Combinazioni del mobile | Quando |
|---|---|---|---|
| **0 Posizioni** (default) | la tabella com'è: una combinazione per posizione, con le sollecitazioni concomitanti vere | una per posizione | verifica |
| **1 Inviluppo** | in ogni stazione massimo e minimo di N, V e M fra le posizioni, combinati negli 8 angoli N±, V±, M± | 8 per combinazione base | dimensionamento: copre ogni posizione, sempre a favore di sicurezza |
| **2 Concomitanti** | in ogni stazione la posizione del massimo e del minimo di N, di V e di M, ciascuna con tutte le sue sollecitazioni | 6 per combinazione base | dimensionamento: preciso sulla grandezza che governa, ma non garantito a favore di sicurezza per l'interazione N–M |

Come funziona:
- Il componente non calcola forze: riscrive la tabella. Ogni gruppo di righe «combinazione base + una posizione» diventa 8 (o 6) righe sulle colonne `prefisso@N+V+M+` … `prefisso@Mmin`.
- Build BridGHe Model riconosce quelle colonne e crea le azioni d'inviluppo, nella fase delle posizioni, dai loro risultati:
  - ogni posizione è ricampionata sull'unione delle stazioni del gruppo, con la stessa regola dell'unione delle azioni;
  - poi si prendono gli estremi stazione per stazione.
- Le verifiche usano N, V e M del piano N–Mx–Vy. Le altre componenti (torsione, flessione e taglio fuori piano) sono quelle della posizione del momento che governa.
- Per le reazioni degli irrigiditori agli appoggi conta la compressione massima fra le posizioni.
- La lista dei messaggi di Build BridGHe Model dice quali posizioni sono sostituite e da quali azioni.

Negli esempi correnti (50 REV006, 99 REV007) il componente c'è, con una Value List impostata su **Posizioni**: i risultati sono quelli della 0.6.1.

Perché un componente nuovo e non un ingresso di Build BridGHe Model: Grasshopper apre una definizione salvata senza un ingresso aggiunto in coda con l'errore «Input parameter chunk is missing. Archive is corrupt.».

## Super Element Check senza «Tau b»

- L'uscita «Tau b» era NaN dappertutto: la gPC non ne ha un equivalente verificato.
- Le tensioni tangenziali dell'anima sono nei risultati di Tension Check (punti 6 e 7, nella tensione ideale). Il taglio all'interfaccia e i pioli sono nel Detail Check.
- «Tension Forces» è ora la seconda uscita.
- Una definizione salvata prima della 0.7.0 la legge dalla terza uscita salvata, con il suo identificativo e i suoi collegamenti.
- Nelle definizioni del catalogo l'uscita è stata tolta direttamente nel file: il resto è byte per byte quello della 0.6.1.

## Bridge Detail Check

- Le sette liste delle verifiche si riempiono solo se qualcosa le legge: nomi, domande, resistenze, unità, rapporti, esiti, avvisi. Section result porta le stesse verifiche.
- Un Detail Check collegato direttamente al Super Element Check mostra un'indicazione: i dettagli (pioli, irrigiditori, taglio dell'anima) servono solo nelle sezioni notevoli, con Bridge Notable Stations → Bridge Detail Stations.

## Tempi (Ponte sul Pescara, 114 posizioni, definizione risolta)

TEMPI_PESCARA

## Straus7

Prove dell'API sul Pescara (`INT\rapporti\bridghe-070-20261121\STRAUS7.md`):
- Il solutore costa 0,6–2 s per modello; con più thread non cambia niente.
- I solutori in parallelo funzionano, ma guadagnano 2–3 s.
- Il tempo vero è nel ricalcolo delle sezioni che Straus7 fa per ogni proprietà: circa 35 s sui 5 modelli. Anche questo, in parallelo, guadagna poco.
- Non ho cambiato il convertitore.

## Prove

- `bridge-moving-envelope`, nuova:
  - le tabelle dei tre modi e i rifiuti (due posizioni nella stessa riga, prefisso assente, inviluppo di un inviluppo);
  - gli angoli che contengono ogni posizione;
  - le concomitanti che sono il campione di una posizione.
- `slab-reinforcement`: Super Element Check con due uscite, e lettura di un archivio della 0.6.x con «Tau b».
- Nativi 50 REV006 e 99 REV007 nei tre modi. Con Posizioni le righe sono identiche alla 0.6.1; con Inviluppo e Concomitanti l'utilizzo massimo delle tensioni è lo stesso, nella stessa sezione.
