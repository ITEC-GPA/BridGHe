# Ritiro del calcestruzzo

`Creep And Shrinkage Concrete` restituisce ora un valore utilizzabile al posto di `NaN`. L'uscita `εcs` è l'incremento negativo di contrazione tra `Reference age` e `t`. Lo stesso filo va a `Composite Shrinkage Actions` e a `Bridge Shrinkage Action`. Un'unica colonna della combinazione deve moltiplicare effetti FEM e autotensioni.

Il modello è quello della **EN 1992-1-1:2004**, 3.1.4 e allegato B, per calcestruzzo ordinario e umidità costante. La fonte tecnica verificata è [JRC Handbook 3, allegato proprietà dei materiali, pp. 18–19 e 30](https://eurocodes.jrc.ec.europa.eu/sites/default/files/2021-12/handbook3.pdf). Questa implementazione non seleziona automaticamente una normativa o un allegato nazionale per il progetto.

Input: resistenza cilindrica positiva in MPa, area in mm², perimetro effettivamente esposto in mm, RH in percentuale ed età in giorni. `Drying start` è la fine della cura. `Reference age` è l'età da cui il ritiro viene trasmesso alla struttura mista. `t=0` conserva la convenzione esistente di tempo infinito. Ulteriori uscite espongono ritiro totale, componente da essiccamento, componente autogena e `h0=2 Ac/u`.

Per una striscia larga 2.500 mm e spessa 300 mm, con entrambe le facce esposte e bordi trascurati: Ac=750.000 mm², u=5.000 mm e h0=300 mm. Con C32/40, cemento N, RH=70%, fine cura e attivazione a 7 giorni, a 365 giorni l'incremento vale **−0,000198939016745**, pari a −198,939 µε. Sono ipotesi dimostrative modificabili, non dati ambientali del progetto dell'utente.

## Librerie e differenze rispetto al metodo precedente

Materiali, fcm, modulo e viscosità continuano a usare GPC. Il calcolo del ritiro è temporaneamente interno a BridGHe, pronto per un successivo spostamento in libreria. Non viene chiamato `GetEpsilonCSInfiniteTime`, che nel sorgente GPC presente ha `kh=100` per h0≤100, ignora il valore restituito dall'interpolazione negli intervalli intermedi e usa le resistenze negative dove la formula richiede i moduli positivi. Non è stato modificato il repository GPC.

Gli otto ingressi e le quattro uscite esistenti mantengono ordine e GUID; sono aggiunti due input temporali e quattro output diagnostici. Le prime tre uscite conservano il calcolo precedente; la quarta passa da `NaN` all'incremento di ritiro. I nuovi input hanno valori iniziali 7 e 0 giorni. I test coprono cementi S/N/R, estremi e interpolazione di kh, RH=100%, origine dei tempi, incrementi additivi e input non validi.
