# Esempio 19 — acciaio, override delle sezioni, merge delle fasi e verifica

Aprire **19-flusso-acciaio-misto.ghx** in Rhino 8 / Grasshopper con la Release aggiornata di BridGHe. Il file contiene i risultati delle quattro analisi Straus già eseguite. Con `CALCOLA 4 MODELLI STRAUS = false`, premere **Run** su `Build BridGHe Model`, poi **Run** su `Super Element Check`. La riapertura con risultati memorizzati è verificata automaticamente.

Per ricalcolare dopo modifiche: verificare i percorsi `.st7` nei quattro componenti di analisi, impostare il toggle a `true`, riportarlo a `false`, poi eseguire merge e verificatore. I percorsi iniziali puntano ad `artifacts/bridge-workflow` in questo checkout; modificarli se si sposta il file. La mesh dei quattro modelli deve conservare ID, geometria, assi, offset e unità. Il valore del prefisso dipende dall'ordine dei collegamenti al merge.

## Schema e dati

Trave continua nel piano su due campate uguali da 12 m, discretizzata in 12 elementi da 2 m. Appoggi verticali a 0, 12 e 24 m, traslazione longitudinale bloccata solo a sinistra. Gradi di libertà fuori piano bloccati; rotazioni nel piano libere ai tre appoggi. Unità **N, mm, MPa**.

- Acciaio S355: E = 210000 MPa, ν = 0,3, α = 1,2×10⁻⁵ /°C. H alta 1600 mm, flange 600×40 mm, anima 16 mm.
- Soletta C30 larga 3000 mm e alta 250 mm. La larghezza è un dato dimostrativo, non una larghezza efficace ricavata da norma.
- Armatura superiore: 3000 mm² totali, asse a 45 mm dalla faccia superiore. Inferiore: 2000 mm² totali, asse a 50 mm dalla faccia inferiore. Acciaio B450, E = 210000 MPa.
- Rapporti modulari assegnati: n0 = 7, n∞ = 18, n ritiro = 12. Nessuna evoluzione temporale è dedotta automaticamente.
- Carichi uniformi dimostrativi: G1 = 8 N/mm, G2 = 4 N/mm, Q = 12 N/mm, verso il basso. Il getto è compreso nel contributo assegnato G1; non si aggiunge automaticamente un secondo peso della soletta.
- Ritiro uniforme del calcestruzzo: εcs = −0,0002 = −200 µε.

## Collegamenti da controllare

`I Section` + `Concrete Slab Section` + materiali → **Composite Beam Property**.

L'uscita acciaio costruisce la beam e il modello di base. `Decompose Model` alimenta le copie della stessa geometria; `Beam Override` sostituisce solo la proprietà delle tre copie miste. Ogni copia riceve i propri carichi, viene riassemblata e analizzata con Straus7 R3.

| Ordine modello | Proprietà | Caso analizzato / aggiunto | Assegnazione al merge |
|---|---|---|---|
| 1 | Only Steel Property | `1-G1_GETTO` | Solo acciaio |
| 2 | n ∞ Beam Section | `2-G2_PERMANENTI` | NInfinite |
| 3 | n0 Beam Section | `3-Q_TRAFFICO` | NInstant |
| 4 | n ritiro Beam Section | `4-RITIRO_FEM` | **Phase n ritiro FEM Load Cases** |
| 4, dopo l'analisi | Sezione mista fisica | `4-RITIRO_PRIMARIO` | Phase Ninf strain Load Cases, con il nuovo n ritiro della sezione |

La combinazione `SLS_DEMO` contiene questi cinque termini con coefficienti unitari. I risultati dei modelli sono incrementali: non sommare stati già cumulativi. Il prefisso identifica l'origine; la fase deriva dal matching esplicito dei nomi, non dal solo indice del modello.

Dopo il merge: `Super Element Filter` → `Composite beam`, con la proprietà mista originale, → `Super Element Check`. I pannelli estraggono σ del calcestruzzo, di entrambe le armature e dell'acciaio inferiore. Le tensioni sono in MPa.

## Ritiro: effetti globali e autotensione

`Composite Shrinkage Actions` usa la geometria gPC, le due armature e n ritiro per ricavare deformazione assiale e curvatura libere della sezione omogeneizzata. La predeformazione va a `Beam Pre Load` con tipo **Strain**; il gradiente termico equivalente va all'asse locale 2 di `Beam Gradient Load`.

Con Aa e Ia riferiti all'acciaio, Ac l'area netta di calcestruzzo, Sc il suo momento statico e yg il baricentro omogeneizzato:

- Ec,eff = Ea / n ritiro;
- N* = Ec,eff Ac εcs; M* = Ec,eff (Sc − Ac yg) εcs;
- ε0 = N* / (Ea Aa); κ = M* / (Ea Ia);
- prestrain Straus = −ε0; gradiente = κ / α.

La rigidezza è quella della stessa proprietà generica usata dall'override. Straus restituisce gli effetti dei vincoli globali. Dopo l'analisi si aggiunge `Composite Beam Strain` con εcs al caso distinto `RITIRO_PRIMARIO`: il verificatore aggiunge l'autotensione una volta sola. Entrambi i contributi usano n ritiro = 12, separato da n∞ = 18.

## Controlli eseguiti

Il test `dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-workflow` genera il GHX, lo riapre, risolve i quattro modelli con la licenza Straus, rilegge A, I11, I22 e J dai file nativi, controlla gli equilibri e confronta reazioni, spostamento libero assiale e momento del ritiro con soluzioni analitiche. Controlla poi che il merge conservi esattamente tutte le sollecitazioni originali e che il verificatore riceva cinque contributi e due file di armatura. Il report effettivo è `validation.json`.

Le sezioni I native dell'acciaio hanno area di taglio calcolata da Straus; le generiche omogeneizzate mantengono la convenzione precedente con aree di taglio nulle, cioè deformabilità a taglio esclusa. Il test delle reazioni include questa differenza: per due campate uguali, β = 3EI/(GA2 L²), R esterna = qL[1/2 − 1/(8(1+β))]. Con area di taglio nulla nel generico si usa β = 0. J nelle fasi miste mantiene il valore gPC della sola H; per la I nativa Straus lo calcola se l'input è nullo. Non sono state modificate queste convenzioni per far coincidere i test.

Sono restituite almeno cinque stazioni per elemento; Straus può aggiungere estremi. L'esecuzione di riferimento contiene **62 stazioni di verifica**. `merged-results.bgmodel` contiene il modello con risultati e fasi riconosciute.

## Preview e limiti

`Display Settings` → `Preview BridGHe Model` mostra le sezioni composte; gli altri componenti sono nascosti nella preview per evitare sovrapposizioni. Il menu **BridGHe → Visualizzazione…** controlla le preferenze globali. `preview-sezioni-composte.3dm` permette di ispezionare gli stessi solidi e le linee di armatura direttamente in Rhino. Tre linee per fila sono schematiche: non rappresentano una distinta ferri.

Esempio elastico con soletta non fessurata e instabilità disattivate. I coefficienti unitari non costituiscono combinazioni normative. Non copre fatica, pioli, instabilità globale, soletta trasversale, torsione/flessione deviata, shear lag o storia reologica evolutiva. L'uscita legacy `Tau b` resta NaN; usare i componenti di taglio e connessione dedicati con le azioni e le opzioni appropriate.
