# 29 — Simple Bridge, traversi pieni, rettilineo

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

Aprire [29-simple-bridge-pieno.ghx](29-simple-bridge-pieno.ghx) con la nuova Release di BridGHe in Rhino 8. Il file genera direttamente la geometria, senza Straus.

![Geometria generata](preview.svg)

Campate 30+40+30 m, tre travi a −4/0/+4 m, soletta larga 12 m e spessa 250 mm. Tutti gli input sono in mm. La pendenza trasversale varia da −2% a +2% e poi +3%; gli appoggi interni hanno sghembo +12° e −8°.

Prove guidate:

1. Portare `Labels` a true: identificare appoggi e giunti.
2. Leggere `Segment Ranges`: il concio 24–36 m attraversa la pila a 30 m.
3. Aprire la raccolta persistente del parametro «Giunti mm»: il ramo `{0}` contiene i giunti comuni, `{2}` aggiunge il giunto a 18 m solo su G2.
4. Modificare il pannello del passo traversi da 5000 a 4000 mm: aumenta il numero di traversi, senza cambiare i giunti assegnati.
5. Modificare la lunghezza massima da 2500 a 1250 mm: aumenta la discretizzazione longitudinale, senza cambiare il numero dei conci.

Gli assi non hanno sezioni o proprietà meccaniche. I punti degli appoggi non sono vincoli. Per i collegamenti successivi a `Beam`, `Element ID` e `Build Model`, consultare la [guida Simple Bridge](../../docs/SIMPLE-BRIDGE.md).

[Report di soluzione e riapertura](validation.json). La preview SVG deriva dalle coordinate effettivamente generate.

## Soletta e link nel graticcio

La definizione aggiornata restituisce `Deck Beams`, `Deck Connections`, `Deck Strip Widths` e `Grillage Geometry`. Il passo della soletta è 2000 mm: 51 linee trasversali, 204 aste (due sbalzi e due campi per linea) e 153 collegamenti alle tre travi. L'asse della soletta è a metà spessore, quindi a −125 mm lungo la normale locale. Il componente `Bridge Rigid Links` è collegato alle eccentricità; non viene lanciata alcuna analisi.

Provare un passo di 3000 mm o una lista di `Slab Beam Stations` di Bridge Mesh Settings: le travi vengono spezzate alle nuove progressive mantenendo i conci. Con `Slab Axis Level = −100` di Bridge Slab Layout si spostano solo soletta a graticcio e attacchi superiori. `Grillage Geometry` richiede una proprietà esplicita per la famiglia 8; per la carpenteria di G1 usare `Bridge Geometry`. La mesh `Deck` resta una preview geometrica. Gli esempi 33–45 mantengono il proprio scopo e usano la carpenteria quando non è prevista la soletta FEM.
