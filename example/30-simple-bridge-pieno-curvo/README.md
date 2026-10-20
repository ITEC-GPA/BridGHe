# 30 — Simple Bridge, traversi pieni, curva

Aprire [30-simple-bridge-pieno-curvo.ghx](30-simple-bridge-pieno-curvo.ghx) con la nuova Release di BridGHe in Rhino 8.

![Geometria generata](preview.svg)

Tracciato circolare di raggio 250 m e lunghezza d'arco 100 m. Le progressive degli appoggi sono 0, 30, 70 e 100 m, misurate sul tracciato. Le tre travi hanno offsets −4/0/+4 m; la soletta è larga 12 m e spessa 250 mm. Tutte le coordinate del file sono in mm.

Prove guidate:

1. Attivare `Labels` e osservare i traversi sulle due linee interne sghembe.
2. Sostituire la curva interna al parametro `Tracciato 3D` con una curva tangente-continua lunga 100000 mm; controllare l'orientamento sinistra/destra.
3. Ridurre `Chord Tolerance` per aumentare la risoluzione della geometria curva.
4. Leggere `Topology`: i punti di attacco degli appoggi sghembi possono avere progressive diverse fra le travi.
5. Verificare che il giunto aggiuntivo a 18 m divida il concio solo su G2.

I conci e i traversi sono indipendenti dal campionamento della curva. La soletta è una geometria di riferimento, non una mesh FEM. Non sono assegnate azioni o condizioni al contorno.

[Guida completa](../../docs/SIMPLE-BRIDGE.md) · [Report di soluzione e riapertura](validation.json).
