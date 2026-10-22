# 31 — Simple Bridge, traversi reticolari a X

Aprire [31-simple-bridge-reticolare.ghx](31-simple-bridge-reticolare.ghx) con la nuova Release di BridGHe in Rhino 8.

![Geometria generata](preview.svg)

Ponte rettilineo a tre campate 30+40+30 m e tre travi a −4/0/+4 m. Reticoli alti 1500 mm, con due pannelli per baia fra travi adiacenti. Assi longitudinali a −1500 mm dalla linea all'estradosso della soletta; tutti gli input in mm.

Prove guidate:

1. Nel componente `Bridge Diaphragms`, lasciare `Pattern = 0` per X. Inizialmente `Connect X = false`: l'incrocio delle diagonali non è un nodo.
2. Impostare `Connect X = true`: ogni diagonale viene spezzata nel centro e le due diagonali condividono il nodo. Confrontare il conteggio di assi e nodi.
3. Impostare `Pattern = 1` per V: le diagonali convergono nel centro del corrente inferiore.
4. Impostare `Pattern = 2` per Warren: diagonali alternate senza montanti interni.
5. In `Diaphragm Members`, distinguere le famiglie 2 correnti superiori, 3 inferiori, 4 montanti, 5 diagonali.
6. Ridurre il passo oppure cambiare i conci: i montanti d'attacco rimangono collegati geometricamente agli assi delle travi.

I montanti non sono link rigidi impliciti. Non sono assegnati rilasci, sezioni, carichi o vincoli. Il reticolo deve ricevere successivamente le proprietà e le condizioni di connessione scelte.

[Guida completa](../../docs/SIMPLE-BRIDGE.md) · [Report di soluzione e riapertura](validation.json).

## Soletta e link nel graticcio

La definizione aggiornata restituisce `Deck Beams`, `Deck Connections`, `Deck Strip Widths` e `Grillage Geometry`. Il passo della soletta è 2000 mm: 51 linee trasversali, 204 aste (due sbalzi e due campi per linea) e 153 collegamenti alle tre travi. L'asse della soletta è a metà spessore, quindi a −125 mm lungo la normale locale. Il componente `Bridge Connections` è collegato in modalità link rigidi; non viene lanciata alcuna analisi.

Provare un passo di 3000 mm o una lista di `Deck Beam Stations`: le travi vengono spezzate alle nuove progressive mantenendo i conci. Con `Deck Beam Level = −100` si spostano solo soletta a graticcio e attacchi superiori. `Grillage Geometry` richiede una proprietà esplicita per la famiglia 8; per la carpenteria di G1 usare `Bridge Geometry`. La mesh `Deck` resta una preview geometrica. Gli esempi 33–45 mantengono il proprio scopo e usano la carpenteria quando non è prevista la soletta FEM.
