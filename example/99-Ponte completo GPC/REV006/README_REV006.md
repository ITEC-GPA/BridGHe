# 99 REV006 — stazioni di verifica e sezioni notevoli (BridGHe 0.6.0)

Aprire **[flusso-acciaio-misto_REV006.ghx](flusso-acciaio-misto_REV006.ghx)** con la Release 0.6.0. Il file deriva dal REV005, conservato, con il comando `--example-stations 99`. Modelli, carichi e combinazioni sono quelli del REV005.

## Cosa cambia

- **I due Super Element Check** (tutte le travi e G2) hanno ciascuno il loro Super Element Check Options. Le instabilità sono quelle di prima e un cursore comune «stazioni di analisi per asta» vale 2, cioè i due nodi.
- **Beam Output Stations** dopo i quattro Bridge FEM Members che alimentano i modelli, con un cursore comune a 5 come prima.
- **I due Detail Check dei pioli** («Pioli sulle azioni di fase» e «Pioli G2») leggono le sezioni notevoli: estremi e metà dei tratti, appoggi e massimi di campata. Ricevono anche i punti degli irrigiditori, cioè gli appoggi e gli intermedi ogni 2 m, così i selettori a valle trovano le loro stazioni.
- Gli irrigiditori agli appoggi e intermedi leggono come prima dai Detail Check dei pioli.
