# BridGHe 0.7.2

Quattro miglioramenti della creazione parametrica del ponte, senza cambiare i risultati: le proposte 1, 2, 4 e 8 dell'analisi del 21 novembre 2026, approvate dall'utente («per tutti i punti»). I testi nuovi del plug-in (descrizioni, messaggi, etichette) sono in inglese, come i nomi dei componenti; i messaggi italiani esistenti restano com'erano.

Le librerie GPC sono le stesse (set ufficiale **BA** del 18 novembre 2026).

## Novità

### 1. Avviso sulle unità del tracciato

Bridge Support Stations e Bridge Alignment Curve danno un avviso arancione, che non blocca il calcolo:
- se il documento Rhino non è in millimetri, con il fattore calcolato dall'unità effettiva («The Rhino document is in metres: … 1000 times smaller …»);
- se il tracciato è più corto di 1 m, anche senza documento («Alignment 85 mm long: probably drawn in metres …»).

Non c'è nessuna conversione: BridGHe legge sempre le coordinate in mm.

### 2. Etichette nella preview di Bridge Grillage Mesh

Con Labels = True, oltre agli appoggi (S2/G1) e ai giunti (G1/J3) di prima, la preview mostra:
- G1…Gn prima dell'inizio e dopo la fine di ogni trave, nel colore del primo e dell'ultimo concio;
- l'ID di ogni concio a metà del concio (G2-C3, lo stesso di Bridge Read Section Excel);
- una freccia con il verso del tracciato e «Start 0.000 m» all'inizio;
- la progressiva di ogni appoggio fuori dal bordo sinistro («S2 · 35.000 m»);
- Left e Right fuori dai bordi della soletta, nella prima campata.

Cambia solo la preview.

### 4. Excel delle sezioni: file cambiato e rilettura automatica

Bridge Read Section Excel:
- ricorda l'ora e lo SHA-256 del file letto e l'ora della lettura, e li salva nel GH con la tabella;
- a ogni soluzione confronta solo l'ora del file, senza leggerlo: se il file è stato salvato dopo, dà l'avviso «Sezioni_conci.xlsx saved at 14:32, after the last read at 12:10: press Run.» e tiene la tabella letta;
- nuovo ingresso facoltativo **Auto Read** (in fondo, default False): con True rilegge il file da solo quando viene salvato (file watcher di Grasshopper) o quando lo trova cambiato. Una rilettura automatica non riuscita tiene la tabella di prima, con un avviso; Run la svuota come prima.

Il file si legge anche se è ancora aperto in Excel (FileShare.ReadWrite); un file bloccato in modo esclusivo è rifiutato come prima. Le tabelle salvate dalle versioni precedenti non hanno l'ora: il confronto parte dalla prima lettura con la 0.7.2.

### 8. Famiglie per nome

- Bridge Section Schedule ha i nomi delle famiglie sull'ingresso Family (tasto destro), come Bridge Family Property.
- Una Value List collegata all'ingresso Family dei due componenti viene compilata con i nomi delle famiglie e tiene il valore selezionato. Una lista con valori che non sono famiglie resta com'è.
- I valori restano i codici interi di prima.

## Esempi

- **50 REV006**: l'ingresso Auto Read (False) di Bridge Read Section Excel è aggiunto nell'XML (`splice072.py`); il resto del file è byte per byte quello della 0.7.1.
- Gli altri esempi non hanno Bridge Read Section Excel e non cambiano.

## Tavolozza

- Nuova riga: Auto Read (Bridge Read Section Excel, ingresso 2). Descrizioni nuove: Labels (Bridge Grillage Mesh), Family (Bridge Section Schedule, Bridge Family Property). Nessun GUID cambia; 232 componenti e parametri, 1.909 porte.
- Baseline delle interfacce (`migration/port-contract-baseline.json`) aggiornata per i quattro file con le porte cambiate.

## Prove

- Nativi di 50 REV006 e 99 REV007 nei tre modi, sul commit `715a5fe`: righe e componenti identici alla 0.7.1 in tutti e sei i casi; cambia solo l'impronta del 50 (`10B5433C…`). Riferimenti in `tests/BridGHe.Smoke/Fixtures/native-gate-070/references-072.json`, righe complete in `P2B\results\t072-native`.
- Gate completo confrontato con le catture della 0.7.0: le 107 regressioni passano; replay delle quattro corsie, scansione e payload uguali (nessuna voce diversa o nuova).
- Prove nuove dentro le regressioni esistenti (il numero resta 107): unità e lunghezza del tracciato, etichette della preview (bridge-alignment-inputs), file cambiato e Auto Read (bridge-segment-excel), famiglie per nome (bridge-assignment).
- Controllo di allineamento delle 49 definizioni del catalogo: tutte hanno le porte della 0.7.2.
- Immagine delle etichette dell'esempio 49, disegnata fuori schermo dai dati della preview: `BridGHe.Smoke --labels-capture <definizione.ghx> <immagine.png>`.
