# Fasi e azioni incrementali

Il flusso Grasshopper è `Straus R3 Analysis → Bridge Action / Bridge Shrinkage Action → Bridge Phase → Build BridGHe Model → Composite Beam → Super Element Check`.

## Oggetti e collegamenti

| Componente | Significato |
|---|---|
| Bridge Action | ID stabile, modello analizzato, caso sorgente, comportamento Steel / Long term / Short term. Selezione facoltativa dei Beam IDs. |
| Bridge Shrinkage Action | ID stabile, incremento di deformazione della soletta, modello/caso degli effetti secondari. Un solo coefficiente moltiplica entrambi. Response seleziona n ritiro oppure n0 per una deformazione istantanea imposta. |
| Bridge Phase | Numero, nome, stato della sezione, lista di azioni e giorno facoltativo. I numeri ordinano le fasi; non rinominano i casi. |
| Build BridGHe Model | Models, Phases, Combinations, Calculation. Restituisce modello, riepilogo delle fasi e diagnostica. |

Le combinazioni hanno una colonna per **Action ID**, senza prefissi derivati dalla posizione del modello. Esempio:

```text
                G1  G2  Q  RITIRO
SLS_DEMO        1   1   1  1
TEST_RIDOTTO    1   1   0  0.7
```

Nel secondo esempio il coefficiente 0.7 si applica sia a `RITIRO [FEM]` sia a `RITIRO [strain]`. Sono coefficienti didattici, non combinazioni normative. I nomi interni permettono di risalire alla sorgente; non devono essere scritti a mano nella tabella.

La contrazione ha segno negativo e unità adimensionale: −200 µε = −0.0002. Le azioni sono **incrementali**: −120 µε seguite da −80 µε danno −200 µε, non −320 µε. `Primary Only` deve essere esplicitamente attivo per omettere il modello FEM: questa scelta omette gli effetti secondari, non li calcola automaticamente. La deformazione primaria e i carichi equivalenti per Straus devono derivare dallo stesso input.

`Models` fornisce geometria e riferimenti; ogni azione porta la propria sorgente analizzata. L'ordine della lista dei modelli non identifica più i casi. Sono richiesti N-mm-MPa, Beam IDs univoci, geometria e orientamento coerenti e campionamento compatibile. Campioni mancanti, risultati non finiti, ID duplicati e casi non trovati producono errori espliciti; non viene interpolato attraverso salti delle sollecitazioni.

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
