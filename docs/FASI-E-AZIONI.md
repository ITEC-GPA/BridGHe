# Fasi e azioni incrementali

Il flusso Grasshopper è `Straus R3 Analysis → Bridge Action / Bridge Shrinkage Action → Bridge Phase → Build BridGHe Model → Composite Beam → Super Element Check`.

## Oggetti e collegamenti

| Componente | Significato |
|---|---|
| Bridge Action | ID stabile, modello analizzato, caso sorgente, comportamento Steel / Long term / Short term. Selezione facoltativa dei Beam IDs. |
| Bridge Shrinkage Action | ID stabile, incremento di deformazione della soletta, modello/caso degli effetti secondari. Un solo coefficiente moltiplica entrambi. Response seleziona n ritiro oppure n0 per una deformazione istantanea imposta. |
| Bridge Phase | Numero, nome, stato della sezione, lista di azioni e giorno facoltativo. I numeri ordinano le fasi; non rinominano i casi. |
| Build BridGHe Model | Models, Phases, Combinations, Calculation. Restituisce modello, riepilogo delle fasi e diagnostica. |
| Bridge Combination Excel | Crea il template XLSX da Models e Phases e rilegge i coefficienti compilati. Table si collega a Combinations del Build. |

## Template Excel da modelli e fasi

In **BridGHe → 04-Definitions**, inserire **Bridge Combination Excel**. Collegare gli stessi `Models` e `Phases` del Build; ogni azione mantiene il proprio modello sorgente, anche se non è elencato in Models. Non vengono aggiunti automaticamente casi non associati alle fasi.

1. Impostare `File` su un percorso `.xlsx`. Un percorso relativo viene risolto rispetto al GH salvato; per un documento non salvato serve un percorso assoluto.
2. Lasciare `Mode = 0` e premere **Run**: vengono creati `Combinations` e `Actions`. `Rows` vale 20 per default. Un file esistente è protetto, salvo `Overwrite = True` esplicito.
3. In `Combinations`, scrivere il nome di ogni combinazione nella prima colonna e un coefficiente numerico per **ogni** azione. Usare **0 esplicito** per un'azione inattiva. Le righe completamente vuote vengono ignorate. Le intestazioni sono già corrette; non servono prefissi.
4. Conservare `Actions` invariato: riporta numero/nome/stato/giorno della fase, modello e caso sorgente, comportamento, strain adimensionale, selezione delle aste e Primary Only. `[]` nella selezione significa tutte le aste.
5. Salvare Excel, impostare `Mode = 1` e premere **Run**. Collegare `Table` direttamente a `Build BridGHe Model → Combinations`, quindi premere Run sul Build.

La riapertura del GH e la normale ricomputazione non leggono né scrivono il file: richiedono Run. Dopo una modifica agli input o un errore di lettura non viene restituita la vecchia tabella. Quando si modifica il contenuto Excel, premere nuovamente Run in lettura e poi sul Build.

Il lettore controlla intestazioni esatte e univoche, nomi delle combinazioni, coefficienti mancanti/non finiti e corrispondenza del foglio Actions con le fasi correnti. È consentito riordinare le colonne di Combinations. Se cambia una fase o l'associazione dell'azione, creare un nuovo template e trasferire consapevolmente i coefficienti. Il controllo delle associazioni non certifica la geometria o la correttezza dei risultati FEM: queste verifiche restano al Build e ai controlli del modello.

Si possono preparare i template anche prima dell'analisi se il caso è definito nel modello: viene emesso un avviso per i risultati mancanti. Per le formule numeriche il lettore usa il valore salvato da Excel e lo segnala: **ricalcolare e salvare prima di Read**; il plugin non esegue le formule. Le formule senza risultato salvato e gli errori Excel vengono rifiutati. Nomi e intestazioni devono essere testi letterali.

Il componente non assegna coefficienti normativi. Anche nomi come `SLU_01` sono etichette: non selezionano lo stato limite del checker; attualmente Super Element Check usa `Rare`. Il generatore automatico di combinazioni è un'attività successiva.

L'[esempio 46](../example/46-combinazioni-excel/README.md) contiene template vuoto, workbook dimostrativo compilato e collegamento Excel → Build con ritiro FEM + strain.

Le combinazioni hanno una colonna per **Action ID**, senza prefissi derivati dalla posizione del modello. Esempio:

```text
                G1  G2  Q  RITIRO
SLS_DEMO        1   1   1  1
TEST_RIDOTTO    1   1   0  0.7
```

Nel secondo esempio il coefficiente 0.7 si applica sia a `RITIRO [FEM]` sia a `RITIRO [strain]`. Sono coefficienti didattici, non combinazioni normative. I nomi interni permettono di risalire alla sorgente; non devono essere scritti a mano nella tabella.

La contrazione ha segno negativo e unità adimensionale: −200 µε = −0.0002. Le azioni sono **incrementali**: −120 µε seguite da −80 µε danno −200 µε, non −320 µε. `Primary Only` deve essere esplicitamente attivo per omettere il modello FEM: questa scelta omette gli effetti secondari, non li calcola automaticamente. La deformazione primaria e i carichi equivalenti per Straus devono derivare dallo stesso input.

`Composite Beam Strain` è **obsoleto**, ma resta disponibile con lo stesso GUID, gli stessi ingressi/uscite e lo stesso calcolo per le definizioni esistenti. Mostra la dicitura `Obsolete` e un avviso con il percorso consigliato: **Bridge Shrinkage Action → Bridge Phase → Build BridGHe Model**. Il Build elimina i carichi diretti dei modelli sorgente e ricostruisce gli incrementi dalle azioni: uno strain aggiunto con il vecchio componente a monte del Build non viene importato automaticamente. `Composite Shrinkage Actions` resta invece attuale: produce i carichi equivalenti per l'analisi Straus; il caso FEM calcolato e lo stesso incremento di deformazione confluiscono in `Bridge Shrinkage Action`.

`Models` fornisce geometria e riferimenti; ogni azione porta la propria sorgente analizzata. L'ordine della lista dei modelli non identifica più i casi. Sono richiesti N-mm-MPa, Beam IDs univoci, geometria e orientamento coerenti. Le stazioni interne possono differire: il merge conserva la loro unione e interpola ciascun caso dai suoi campioni originali (N/V/T lineari, momenti quadratici basati sul taglio e vincolati ai due momenti FEM). Sono necessari i campioni agli estremi; non si estrapola. Duplicati, step ambigui, risultati non finiti e casi non trovati producono errori espliciti. I carichi concentrati richiedono la gestione dei due lati del salto. Metodo, limiti e test sono descritti in [Allineamento delle stazioni FEM](INTERPOLAZIONE-STAZIONI.md).

## Due metodi numerici espliciti

**Calculation = 0, Cumulative**, conserva il precedente metodo di sezione gPC, raggruppando gli incrementi per comportamento. L'ordine è tracciato ma non introduce un nuovo algoritmo di attivazione. Le regressioni numeriche devono restare invariate dove rigidezze e azioni non cambiano.

**Calculation = 1, LinearHistory**, usa `HBridgeHistoryResults.Calculate` della libreria GPC. Segue gli incrementi N–Mx e l'attivazione della soletta, compreso lo stato ConcreteExcluded. Non è un solutore globale di costruzione per il ponte: le sollecitazioni devono essere già ottenute dai corretti modelli Straus. Il giorno è metadato, senza integrazione automatica di viscosità o ritiro nel tempo. La deformazione riportata è geometrica rispetto all'inizio della storia; la tensione del calcestruzzo segue la sua attivazione.

LinearHistory non verifica taglio, pioli o irrigiditori: tau non disponibile è NaN e Bridge Detail Check rifiuta questi risultati, evitando di ricalcolarli con il metodo cumulativo. Il checker H conserva i limiti relativi a torsione e flessione sull'asse minore; le componenti FEM non supportate non vengono cancellate.

## Modifiche dell'interfaccia

- Build BridGHe Model sostituisce gli ingressi di matching manuale con Phases e Calculation; Combinations è il terzo ingresso.
- Bridge Phase ora identifica la fase del workflow. Il componente gPC preesistente si chiama **Bridge Section Increment**.
- Composite Beam Property ha `n ritiro` dopo `n infinito`, prima dei booleani.
- Nuovi componenti e GUID per Action, Shrinkage Action e Phase; il plugin resta BridGHe.
- Non è prevista compatibilità dei vecchi collegamenti di Build BridGHe Model. Gli esempi correnti sono rigenerati; REV001/REV002 dell'utente restano riferimenti storici.

## Esempi e riproduzione

Gli esempi 11–18 usano gli archivi dei risultati nativi esistenti per verificare il nuovo assemblaggio senza avviare Straus. Negli esempi 12–18 n ritiro coincide con n infinito, coerentemente con le analisi storiche. Il secondario e il primario sono ora due contributi distinti associati alla stessa azione. L'esempio 11 mantiene le due deformazioni imposte primarie del precedente modello, con Response rispettivamente n ritiro e n0.

```powershell
dotnet build tests/BridGHe.Smoke -c Release
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --grillage-gh-replay
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-gh-replay all
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-workflow
```

L'ultimo comando prepara REV003 del ponte a tre travi senza eseguire Straus. Aggiungere `--native` per analizzare e controllare il nuovo modello. Nei ponti curvi il blocco per torsione fuori campo è un esito atteso; non rappresenta una verifica completa del ponte.
