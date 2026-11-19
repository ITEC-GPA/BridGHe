# BridGHe 0.4.3

Correzioni nate dalla prova sul viadotto Brecciarola del 19 novembre 2026 (punti 14, 16 e 19 dello studio dei componenti). Le librerie GPC sono le stesse della 0.4.2: set ufficiale **BA** del 18 novembre 2026 (`gpc-release-20261118-ba`), con ReBear 0.4.4 e Arachne 0.5.1.

Le definizioni esistenti si aprono senza modifiche e, finché le sezioni convergono, danno gli stessi numeri della 0.4.2.

## Super Element Check

**Un caso che non converge non annulla più la verifica.**
- Prima bastava una combinazione con la sezione efficace non convergente (tipicamente dove M ≈ 0, agli appoggi e sugli sbalzi) perché il componente desse «Generic error: One or more errors occurred» e nessuna riga.
- L'elenco dei casi citati cambiava fra un calcolo e l'altro: comparivano solo le stazioni che in quel momento erano in corso.
- Ora tutte le stazioni vengono calcolate. Una combinazione fallita lascia un elemento vuoto al suo posto, così l'albero mantiene l'ordine delle combinazioni.
- L'errore dice quante verifiche mancano su quante e in quante stazioni. Poi le elenca in ordine (fino a 50), con trave, stazione, combinazione e messaggio di GPC.
- Il messaggio sotto il componente aggiunge «· n non calcolate».

**Due ingressi facoltativi in coda.** Le definizioni esistenti mantengono i loro collegamenti.
- **Options (O).** Bridge Analysis Options sostituisce le scelte fisse del componente: Eurocodice 4, SLE rara, Class4 e coefficienti di default. Con Options collegato valgono le scelte di instabilità locale delle opzioni; WLI, TFLI e BLI non sono usati, e un'osservazione lo ricorda.
- **ULS Combinations (ULS).**
  - Indica le combinazioni da verificare allo stato limite ultimo: nomi esatti, oppure un prefisso seguito da `*` (per esempio `SLU_*`).
  - Le altre combinazioni restano allo stato limite delle opzioni, quindi SLE rara se Options non è collegato.
  - Così una tabella con righe SLU e righe caratteristiche ha i limiti giusti riga per riga: fyd e VEd/VRd nelle righe SLU.
  - Un nome che non corrisponde a nessuna combinazione è un errore, segnalato prima del calcolo.
  - Il messaggio sotto il componente aggiunge «· SLU n/N».

## Bridge Detail Check

Gli errori di calcolo di una sezione cominciano con trave, stazione e combinazione, per esempio «trave 12, x = 4200 mm, SLU_STR1 + Q_MOV_0028: …». Il componente continua a lasciare un elemento vuoto al posto delle sezioni fallite. Un elemento vuoto in ingresso, per esempio da Super Element Check, dà un'uscita vuota, senza errore.

## Elementi vuoti nei componenti dei risultati

I componenti che leggono le verifiche ora saltano gli elementi vuoti e con un avviso dicono quanti sono. Prima si fermavano:
- **Bridge Detail Stations**: «Object reference not set to an instance of an object». Ne dipendevano le verifiche degli irrigiditori ai giunti e agli appoggi;
- **Bridge Check Summary**: «Supply BridgeStressResult objects from a bridge checker»;
- **Bridge Result Labels**: «Results must contain BridgeStressResult objects»;
- **Bridge Report**: lo stesso messaggio.

Nei dettagli:
- Bridge Result Labels dà un'etichetta vuota agli elementi vuoti, così resta allineata ai risultati; Bridge Check Summary salta entrambi.
- Bridge Check Map salta l'elemento vuoto con il suo ID, la stazione e il caso.
- Nei grafici (Bridge Stress Plot, Shear Plot, Stud Plot, Force Plot e i grafici degli irrigiditori) un elemento vuoto è un valore mancante, come NaN.
- Se tutti gli elementi sono vuoti, l'errore lo dice.

## Prove

- Le regressioni `bridge-check-summary`, `bridge-detail-stations` e `slab-reinforcement` coprono:
  - gli elementi vuoti;
  - la selezione delle combinazioni SLU e l'elenco ordinato dei fallimenti;
  - Options uguali ai default, con gli stessi numeri bit per bit;
  - le combinazioni SLU per nome e per prefisso, e il nome sconosciuto.
- La tabella della palette ha due righe in più, le porte 5 e 6 di Super Element Check; nessun'altra riga cambia. Le due righe sono approvate dall'utente con le correzioni, il 19/11/2026.
