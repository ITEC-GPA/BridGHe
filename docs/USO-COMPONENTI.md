# Uso dei componenti di verifica BridGHe

Audit dei GUID nei **19 esempi GHX** del repository. Assenza dagli esempi non significa componente inutile o non utilizzato nei file dell'utente.

I riferimenti per nome nei sorgenti dei test indicano un uso esplicito; non sono una misura di copertura. Il caricamento e le icone di tutti i componenti sono controllati separatamente dagli smoke test.

## Componenti assenti dagli esempi GHX

| Componente | Gruppo nella tab BridGHe | Riferimenti espliciti nei test |
|---|---|---|
| Bridge Analysis Options | 08-Bridge | No |
| Bridge Box Options | 08-Bridge | No |
| Bridge Connection Fatigue | 08-Bridge | No |
| Bridge Force Plot | 13-Results | Sì |
| Bridge Phase | 08-Bridge | No |
| Bridge Rebar Row | 08-Bridge | No |
| Bridge Section Check | 10-Checks | Sì |
| Bridge Section Geometry | 08-Bridge | No |
| Bridge Shear Plot | 13-Results | No |
| Bridge Shear Results | 13-Results | Sì |
| Bridge Stress Plot | 13-Results | No |
| Bridge Stud Plot | 13-Results | No |
| Bridge Stud Results | 13-Results | Sì |
| Bridge Transverse Reinforcement | 08-Bridge | No |
| Bridge Verification Results | 13-Results | Sì |
| Composite Geometric Property | 05-Properties | No |
| Composite Section Linear Tension Analysis | 10-Checks | No |
| Composite Section Self Tension | 10-Checks | No |
| Creep And Shrinkage Concrete | 08-Bridge | No |
| Section Force | 08-Bridge | No |
| Section H Beam Property | 03-Sections | No |

## Valutazione funzionale

- Il vecchio `CompositeSectionComponent_OBSOLETE`, esposto come secondo `Composite Beam Property`, è stato rimosso: il componente attuale fornisce la proprietà composta e le proprietà FEM di acciaio, n0, n∞ e n ritiro. Nessun esempio versionato usa il GUID rimosso.
- `Composite Section Linear Tension Analysis` è il percorso precedente per azioni assegnate a una sezione. Per risultati FEM il percorso corrente usa `Super Element Check`; per sezioni H, H inclinate e cassoni con fasi esplicite usa `Bridge Section Check`. Il vecchio componente resta disponibile: i tre percorsi non sono sostituiti automaticamente perché una sostituzione potrebbe cambiare i risultati.
- `Section Force` e `Composite Section Self Tension` servono per controlli di sezione manuali. Le proprietà geometriche H e composte servono per interrogare e confrontare le rigidezze. Non sono duplicati dell’export.
- `Concrete Slab Section`, `Composite Beam Property` e `Creep And Shrinkage Concrete` servono a costruire le proprietà: gli esempi precedenti salvano spesso questi dati già costruiti nei parametri. Il nuovo esempio 19 contiene esplicitamente i generatori della soletta e della proprietà mista.
- Geometria, file di armature, fasi, opzioni gPC, fatica, armatura trasversale, cassoni, tau, pioli e grafici sono funzioni complementari del verificatore. L’assenza da un GHX non giustifica la loro eliminazione.

## Componenti presenti negli esempi GHX

| Componente | Esempi |
|---|---|
| Bridge Detail Check | 7 |
| Bridge Intermediate Stiffener | 7 |
| Bridge Studs | 7 |
| Bridge Support Stiffener | 7 |
| Build BridGHe Model | 9 |
| Composite Beam Property | 1 |
| Composite Beam Strain | 9 |
| Composite Section Linear Tension Results | 9 |
| Composite beam | 9 |
| Concrete Slab Section | 1 |
| Super Element Check | 9 |
| Super Element Filter | 9 |

Dettagli riproducibili, GUID e percorsi: [component-usage.json](component-usage.json). Rigenerazione: `py tools/audit_component_usage.py`.
