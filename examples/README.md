# Dieci esempi verificati con Straus7 R3

È disponibile anche l'[esempio 11: ponte misto a graticcio](bridge-grillage/README.md), con un canvas costruito da componenti per geometria, vincoli, carichi, analisi e MeCheck2, 357 controlli e tavole PDF. I dieci benchmark qui sotto restano casi elementari separati.

Aprire i file della cartella [grasshopper](grasshopper) in Rhino 8 con BridGHe installato. Ogni definizione contiene un modello FEM incorporato, il componente di analisi, `Decompose Model` e i componenti di lettura di spostamenti e sollecitazioni. Impostare il pannello del percorso `.st7` in una cartella esistente; premere **Run** sul componente di analisi. Nel caso mobile impostare il toggle **Run=true**, poi riportarlo a false.

Le definizioni partono con l'analisi automatica disattivata. Geometria, proprietà, carichi e sequenze sono incorporati nel parametro `Modello`; le ricette modificabili sono in [NativeExamples.cs](../tests/BridGHe.Smoke/NativeExamples.cs). Questi sono esempi della catena FEM e della gestione delle fasi: il modello iniziale è internalizzato, non costruito da una rete di slider. Per sostituirlo con una propria costruzione parametrica, collegare l'uscita di `Build Model` all'ingresso `Model`.

Tutti i confronti usano **N, mm, MPa**. Materiale elastico isotropo: E = 210000 N/mm², ν = 0,3; peso proprio escluso. La trave generica usa A = 60000 mm², I11 = 450000000 mm⁴, I22 = 200000000 mm⁴, J = 100000000 mm⁴, aree di taglio nulle (Euler–Bernoulli). Asse longitudinale X, verticale Z. Spostamenti e reazioni sono globali; i momenti beam mantengono la convenzione Straus BM1/BM2.

| File | Schema e azioni | Confronti principali |
|---|---|---|
| [01-cantilever](grasshopper/01-cantilever.ghx) | Mensola L=3000, incastro a sinistra; due forze Z di −600 e −400 N nello stesso caso P; combinazione 1,5P. | uZ(P)=−0,095238095238 mm; RZ=1000 N; reazione MY=−3000000 Nmm. Combinazione moltiplicata per 1,5. |
| [02-simply-supported](grasshopper/02-simply-supported.ghx) | Trave L=6000 in quattro elementi; appoggio/carro nel piano XZ, altri moti fuori piano bloccati; qZ=−1 N/mm. | uZ(L/2)=−0,178571428571 mm; reazioni 3000 N; max \|BM2\|=4500000 Nmm. |
| [03-plate-patch](grasshopper/03-plate-patch.ghx) | Shell rettangolare 1000×500, t=10; UX bloccato sul bordo sinistro, UY in un angolo, moti fuori piano bloccati; 1000 N in X su ciascuno dei due nodi destri. | uX=0,001904761905 mm; contrazione trasversale −0,000285714286 mm; Nxx=4 N/mm; somma RX=−2000 N. |
| [04-moving-load](grasshopper/04-moving-load.ghx) | Trave appoggiata L=6000, 12 elementi; asse mobile da 1000 N verso −Z; 13 divisioni di percorso. | 13 posizioni ai centri delle divisioni; equilibrio e reazione destra per ogni posizione; inviluppo max \|BM2\|=1500000 Nmm. |
| [05-nonlinear-increments](grasshopper/05-nonlinear-increments.ghx) | Mensola con incrementi 0,5P, P, 2P nel solutore NLA; estrazione soltanto di Half e Double. | uZ=−0,047619047619 e −0,190476190476 mm; reazioni e momenti; indice originale degli incrementi. |
| [06-stage-loading](grasshopper/06-stage-loading.ghx) | Fase Steel: P; fase Deck: 1,5P; stesso gruppo strutturale attivo. | Risposta ai livelli totali P e 1,5P; nomi distinti `Load [Steel]` e `Load [Deck]`. |
| [07-stage-unloading](grasshopper/07-stage-unloading.ghx) | Tre fasi: P → 0 → 0,5P, con struttura invariata. | Ritorno elastico a zero entro tolleranza e successivo ricarico; nessun accumulo errato dei fattori. |
| [08-stage-activation](grasshopper/08-stage-activation.ghx) | Asta assiale incastrata L=3000; prima una trave con P=1000 N, poi attivazione di una seconda trave coincidente e aumento del carico totale a 2P. Morph/Rotate attivi. | Prima u=P L/(EA); dopo u=1,5 P L/(EA), forze 1500 e 500 N. Nessun risultato per la seconda trave prima della nascita. |
| [09-stage-removal](grasshopper/09-stage-removal.ghx) | Sequenza precedente, poi rimozione della seconda trave mantenendo il carico totale 2P. | u=2 P L/(EA)=0,000476190476 mm; unica trave rimasta con N=2000 N; nessun risultato per quella rimossa. |
| [10-stage-selection](grasshopper/10-stage-selection.ghx) | Construction: 0,5G e G; Service: G+0,5Q e 1,35G+1,5Q, G=Q=1000 N verso −Z. | Estratti solo gli incrementi 2 e 4; uZ=−0,095238095238 e −0,271428571429 mm; verifica della fase nativa associata. |

Gli esempi 06–10 costituiscono **cinque test distinti di costruzione per fasi**. Le denominazioni Steel/Deck nell'esempio 06 identificano la sequenza dei carichi: non cambiano automaticamente materiale o rigidezza. I coefficienti dell'esempio 10 sono dati del benchmark, non una definizione completa di combinazioni normative.

## Soluzioni di riferimento e tolleranze

- Mensola con forza terminale: uZ = −P L³/(3EI), RZ=P, MY alla radice=−PL.
- Trave appoggiata con q: uZ(L/2)=−5qL⁴/(384EI), reazioni=qL/2, momento massimo=qL²/8.
- Piastra in trazione uniforme: uX=FL/(Ebt), uY=−νF/(Et), Nxx=F/b.
- Asse mobile: xᵢ=(i−1/2)L/13, Rdestra=P xᵢ/L, Mmax(xᵢ)=P xᵢ(L−xᵢ)/L. La discretizzazione contiene L/2; una discretizzazione pari può non cogliere il massimo esatto.
- Attivazione senza tensioni: rigidezza iniziale k=EA/L, incremento successivo ripartito su 2k; rimozione con redistribuzione al solo elemento rimasto.

Per ogni misura il criterio è `|ottenuto−atteso| ≤ max(tolleranza_assoluta, |atteso|×tolleranza_relativa)`. Default: relativa 1e−5, assoluta 1e−8 nella rispettiva unità. Conteggi e assenze sono esatti. Il confronto con le formule lineari nelle prove NLA è valido per questi piccoli spostamenti; sono verificate anche convergenza di tutti gli incrementi e appartenenza alle fasi. Non è una prova di convergenza generale in campo fortemente non lineare.

## Cinque contributi di fase MeCheck2

[NativeBridgePhases.cs](../tests/BridGHe.Smoke/NativeBridgePhases.cs) aggiunge un confronto Straus → `BridgePhaseAssembler` → `BridgeCheckAdapter` → gPC. Tre coppie terminali su una mensola producono momenti costanti noti. Il modello isostatico serve a validare lettura, segno e passaggio delle azioni; non simula la variazione di rigidezza globale della sezione composta.

Sezione H alta 300 mm, flange 200×20 mm e anima 260×10 mm; soletta 1000×200 mm senza armatura. Acciaio E=210000 MPa, cls C30 con modulo ricavato dall'oggetto materiale gPC. Quote: interfaccia y=0, acciaio da −300 a 0, soletta da 0 a 200. Fasi accumulate:

1. Solo acciaio: momento positivo 1 kNm.
2. Composta a lungo termine: incremento 2 kNm, φ efficace=2.
3. Composta istantanea: incremento 3 kNm, φ efficace=0.
4. Ritiro imposto al cls −100 microstrain con φ efficace=2.
5. Ritiro imposto al cls −50 microstrain con φ efficace=0.

Si verificano gli assi nativi, il verso della deformata, le azioni trasferite e quattro tensioni di lembo dopo ciascuna fase. Tolleranza sulle tensioni: relativa 1e−6, assoluta 1e−7 MPa. Le formule indipendenti usano n=Es/[Ec/(1+φ)], baricentro e inerzia della sezione omogeneizzata; per il ritiro risolvono il sistema 2×2 di equilibrio assiale e flessionale con deformazione libera imposta al solo calcestruzzo.

Il motore gPC corrente integra le flange della sezione composta sulle loro linee medie: nel riferimento numerico delle fasi composte si sottrae `2 b tf³/12` all'inerzia propria dell'acciaio. Questa idealizzazione è dichiarata e non assorbita aumentando la tolleranza. Nella fase di solo acciaio si usa l'inerzia geometrica completa. Esclusi instabilità locale e armature in questo benchmark; non sono verifiche complete di resistenza di un ponte. Il ritiro è imposto, non calcolato da un modello temporale normativo.

## Riproduzione e risultati

```powershell
.\tools\build.ps1 -Native -Examples -Package
# singoli casi
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 08
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-native
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 08
```

Serve la licenza API Straus7 R3; le prove native falliscono esplicitamente se non disponibile. Il test Grasshopper avvia Rhino.Inside in un processo isolato, carica solo Grasshopper e BridGHe, scrive e riapre ogni `.ghx`, esegue il solutore attraverso il componente e controlla anche gli output dei componenti risultati. Non modifica i plugin o le impostazioni del profilo utente. L'isolamento usa due membri interni di Rhino 8 nel solo generatore di test e segnala un errore se cambiano.

I comandi si eseguono nella radice del repository sorgente. Il pacchetto installabile contiene plugin, definizioni e report; per modificare e compilare le ricette C# usare il repository completo. Collaudo del 17 ottobre 2026: 121 confronti nativi, 111 confronti attraverso Grasshopper e 30 controlli sulle fasi MeCheck2, tutti superati.

- [Report dei dieci benchmark](validation/native.json): valori attesi, ottenuti, tolleranze ed esiti.
- [Report delle cinque fasi composte](validation/mecheck2.json).
- [Report delle definizioni Grasshopper](validation/grasshopper.json).
- Modelli `.st7`, risultati `.LSA/.NLA`, log del solutore e archivi `.bgmodel`: generati in `artifacts/examples/<data-ora>` e raccolti in `artifacts/BridGHe-native-examples.zip`.
- Report e definizioni sono versionati; i file binari Straus sono artefatti rigenerabili. Le geometrie dei benchmark sono originali e non derivano da progetti reali.

## Quattro nuovi ponti: esempi 12–15

[Definizioni GHX, parametri, tre fasi + ritiro, dettagli gPC e risultati](bridge-suite/README.md). Due continui rettilinei e due curvi; [tavole riepilogative](bridge-suite/quattro-ponti.pdf).
