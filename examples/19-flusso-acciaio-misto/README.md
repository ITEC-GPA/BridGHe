# Esempio 19 — ponte a graticcio e workflow a fasi

Aprire **[19-flusso-acciaio-misto_REV003.ghx](19-flusso-acciaio-misto_REV003.ghx)** in Rhino 8 / Grasshopper. Il file senza suffisso contiene la stessa definizione corrente. REV001, REV002 e i fogli Excel dell'utente sono conservati come riferimenti storici; i loro vecchi collegamenti al merge non rappresentano la nuova interfaccia.

REV003 è preparato e controllato senza nuove analisi Straus. Per ottenere sollecitazioni e grafici: verificare i quattro percorsi .st7, attivare **CALCOLA 4 MODELLI STRAUS**, riportarlo a false, premere **Run** su Build BridGHe Model e poi su Super Element Check. Non sono riutilizzati i risultati della vecchia trave, perché la geometria è cambiata.

## Geometria e ipotesi

- Ponte rettilineo a due campate da 30 m, impalcato nominale largo 12 m. Tre travi agli assi y = −4, 0, +4 m.
- Spina centrale H2000: flangia inferiore 800×40, superiore 600×30, anima 15 mm. Laterali H1600: inferiore 600×40, superiore 500×30, anima 16 mm.
- 24 elementi da 2.5 m per longitudinale; traversi H800 (flange 300×20, anima 12 mm) ogni 5 m. Totale 75 nodi e 98 beam.
- Appoggi verticali a x = 0, 30, 60 m su tutte le travi. Tx bloccata nel primo appoggio esterno; Ty nel primo e nell'ultimo della stessa trave. Rotazioni libere; nessun vincolo artificiale ai nodi interni.
- Soletta C32/40, spessore 250 mm, larghezza efficace assegnata 3000 mm per trave; è un parametro didattico, non una larghezza normativa. La soletta trasversale non è modellata: i traversi mantengono l'acciaio in tutte le fasi.
- Due file di armature B450: superiore 3000 mm² a 45 mm, inferiore 2000 mm² a 50 mm dalle rispettive facce.
- Acciaio E = 210000 MPa; n0 = 7, n∞ = 18, n ritiro = 12. Strain di ritiro −200 µε. Unità N, mm, MPa.

## Flusso

Il modello di base è assemblato in acciaio. Le copie dei longitudinali ricevono le proprietà n∞, n0 e n ritiro tramite **Beam Override**. Materiali, geometrie delle sezioni, parametri e input delle fasi sono componenti visibili e modificabili.

| Numero | Azione | Modello incrementale | Dati |
|---|---|---|---|
| 10 | G1 | Acciaio | 33 N/mm verso il basso per trave |
| 20 | G2 | Misto n∞ | 10 N/mm verso il basso per trave |
| 30 | Q | Misto n0 | 10 N/mm verso il basso per trave |
| 40 | RITIRO | Misto n ritiro | Prestrain e gradiente equivalenti + strain primario −0.0002 |

I carichi sono dimostrativi, senza peso proprio automatico. `Composite Shrinkage Actions` genera prestrain e gradiente da geometria, armature e n ritiro. `Bridge Shrinkage Action` associa il caso calcolato RITIRO_FEM allo stesso input di deformazione: il coefficiente RITIRO della combinazione scala entrambi. Non aggiungere un secondo Composite Beam Strain.

La matrice contiene SLS_DEMO e due prove con fattori maggiorati ripresi da REV002 (Q×20 e G2×10): **non sono combinazioni normative**. Il vento resta nel REV002 originale: la nuova verifica dimostrativa riguarda la spina centrale sotto azioni simmetriche, compatibili con il campo N–M–V del checker H. Torsione e flessione deviata delle travi laterali non sono cancellate dal FEM e richiedono verifiche dedicate.

Sono collegati preview del modello, delle sezioni composte e due grafici SVG con visualizzatori sul canvas. Il calcolo parte in modalità Cumulative; LinearHistory è descritto nella [guida fasi e azioni](../../docs/FASI-E-AZIONI.md).

## Riproduzione

```powershell
dotnet build tests/BridGHe.Smoke -c Release
# Generazione, riapertura e controlli della geometria, senza Straus:
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-workflow
# Con licenza, nuove analisi e controlli di equilibrio:
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-workflow --native
```

Il report corrente è `validation-REV003.json`; `validation.json`, `merged-results.bgmodel` e le immagini della vecchia trave sono risultati storici, non risultati di questo graticcio. Gli input e i risultati nuovi sono identificati REV003 in `artifacts/bridge-workflow`.

Restano da verificare nativamente il nuovo graticcio e la sensibilità alla mesh. L'esempio non comprende una verifica completa di ponte: mancano combinazioni di progetto, traffico mobile sul graticcio, azioni orizzontali, fatica, connessione, instabilità e soletta locale.
