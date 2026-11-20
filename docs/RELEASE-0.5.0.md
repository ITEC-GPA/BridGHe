# BridGHe 0.5.0

Soletta delle sezioni miste data con passo e diametri, invece che con le aree delle file. È la richiesta del 20 novembre 2026: «per la soletta gli input devono essere base, altezza, passo e diametro armature sopra e sotto» (punto 20 dello studio dei componenti sul viadotto Brecciarola). Scelta dell'utente: solo BridGHe, con il passo comune alle due file e i diametri liberi.

Le librerie GPC sono le stesse della 0.4.3: set ufficiale **BA** del 18 novembre 2026 (`gpc-release-20261118-ba`), con ReBear 0.4.4 e Arachne 0.5.1.

## Concrete Slab Section con passo e diametri

**Componente nuovo**, con un ID nuovo:

| Ingresso | Significato | Default |
|---|---|---|
| Height (H) | spessore della soletta [mm] | — |
| Width (W) | larghezza della soletta [mm], cioè la larghezza efficace della trave | — |
| Pitch (P) | passo delle barre, uguale per le due file [mm] | 200 |
| Top diameter (TD) | diametro delle barre superiori [mm]; 0 = nessuna fila | 0 |
| Top axis distance (TA) | distanza dell'asse delle barre superiori dall'estradosso [mm] | 50 |
| Bottom diameter (BD) | diametro delle barre inferiori [mm]; 0 = nessuna fila | 0 |
| Bottom axis distance (BA) | distanza dell'asse delle barre inferiori dall'intradosso [mm] | 50 |

**Area di una fila.** È l'area sulla larghezza della soletta: A = (W/P)·π·d²/4. Per esempio Ø16/100 su 4050 mm dà 8143 mm², come nella relazione del Brecciarola.

**Le barre della sezione delle verifiche.**
- Prima ogni fila era rappresentata con 3 barre equivalenti, ciascuna con un terzo dell'area. Una fila fitta vicino a una faccia veniva rifiutata: Ø16/100 su 4500 mm dava barre di Ø62, e con l'asse a 30 mm dall'intradosso il raggio usciva dalla soletta.
- Ora ogni fila ha W/P barre (arrotondato), di diametro equivalente √(4A/(nπ)), cioè circa il diametro vero. Le due file hanno lo stesso numero di barre perché il passo è comune.
- Le verifiche che usano il diametro lo leggono ora quasi vero:
  - copriferro;
  - controlli dei pioli rispetto alle armature;
  - distanza fra le file.
- La sezione delle analisi è la stessa di prima (una barra per fila con l'area intera): le proprietà omogeneizzate e i modelli FE non cambiano.

**Controlli del componente.**
- Passo positivo e non minore dei diametri; al più 500 barre per fila (W/P).
- Ogni barra dentro la soletta; la fila superiore sopra quella inferiore, con le barre separate.

**Il componente con le aree resta**, nascosto nella palette e segnato come superato, con il suo ID e i suoi ingressi. Le definizioni che lo usano, per esempio le revisioni precedenti degli esempi e i file del Brecciarola fino alla REV005, si aprono e danno gli stessi numeri di prima.

## Esempi

Nelle definizioni correnti degli esempi 19 (REV003), 49, 50 (REV004) e 99 (REV005) il componente con le aree è sostituito da quello nuovo. Passo e diametri sono scelti in modo da tenere le aree di prima:

| Esempio | Prima | Ora |
|---|---|---|
| 19 | 3000 mm² sopra, 2000 sotto, su 3000 mm | P = 201,0619, Ø16 sopra, Ø13,0639 sotto |
| 49 | 3500 + 3500 mm² su 3500 mm | P = 201,0619, Ø16 sopra e sotto |
| 50 | 3500 + 3500 mm² su 3000 mm | P = 172,3388, Ø16 sopra e sotto |
| 99 | Ø16/10 cm calcolato con un cluster | P = 100, Ø16 sopra e sotto (i due cluster e il secondo slider del passo sono tolti) |

I passi con i decimali tengono le aree di prima. Per valori tondi si può scegliere, per esempio, Ø16/200: le aree cambiano di qualche per cento.

## Prove

Le regressioni `slab-reinforcement` e `composite-property-phases` coprono:
- aree, numero di barre e regole di validità;
- il componente nuovo e quello superato;
- la fila di Ø16/100 su 4500 mm con l'asse a 30 mm: ora è accettata, con 3 barre equivalenti era rifiutata;
- le tensioni con le barre vere, uguali a quelle con 3 barre entro 1e-3 MPa;
- il numero di barre passato da Composite Beam Property alla sezione delle verifiche.

La tabella della palette ha:
- una riga in più: il componente nuovo e le sue 8 porte;
- la riga del componente con le aree cambiata: nome, descrizione, esposizione nascosta, superato.
