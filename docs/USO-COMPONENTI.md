# Uso dei componenti BridGHe

Audit di **183 componenti** nei **52 GHX correnti** elencati in `example/catalog.json`, comprese le definizioni aggiuntive. Copie archiviate e file personali non catalogati sono esclusi. Assenza dagli esempi non significa componente inutile o non utilizzato nei file dell'utente.

I riferimenti per nome nei sorgenti dei test indicano un uso esplicito; non sono una misura di copertura. Il caricamento e le icone di tutti i componenti sono controllati separatamente dagli smoke test.

## Componenti assenti dagli esempi GHX

| Componente | Gruppo nella tab BridGHe | Riferimenti espliciti nei test |
|---|---|---|
| Align beam axes | 14-Tools | No |
| Align plate axes | 14-Tools | No |
| Aluminium | 02-Materials | No |
| Angle Section | 03-Sections | No |
| Beam Distributed Moment | 09-Loads | Sì |
| Beam Geometric Property | 05-Properties | No |
| Beam Point Load | 09-Loads | No |
| Beam Point Moment | 09-Loads | No |
| Beam Releases | 07-Attributes | No |
| Beam Support | 07-Attributes | Sì |
| Beam Taper | 07-Attributes | No |
| Brep Section | 03-Sections | No |
| Bridge Box Options | 08-Bridge | No |
| Bridge Connection Fatigue | 08-Bridge | No |
| Bridge Shear Results | 13-Results | Sì |
| Bridge Transverse Reinforcement | 08-Bridge | No |
| C Section | 03-Sections | No |
| Composite Beam Strain | 09-Loads | Sì |
| Composite Geometric Property | 05-Properties | No |
| Composite Section Linear Tension Analysis | 10-Checks | No |
| Composite Section Self Tension | 10-Checks | No |
| Elements Filter | 14-Tools | Sì |
| Generic Section | 03-Sections | No |
| Group | 04-Definitions | No |
| Hollow Circle | 03-Sections | No |
| Load Patch Type | 07-Attributes | Sì |
| Merge Geometry | 14-Tools | No |
| Merge Model | 06-Model | No |
| Moving Centrifugal Data | 09-Loads | No |
| Moving Distributed Force | 09-Loads | No |
| Moving Heat Source | 09-Loads | No |
| Moving Library Template | 09-Loads | No |
| Moving Load Path | 09-Loads | No |
| Moving Load Template | 09-Loads | No |
| Moving Point Force | 09-Loads | No |
| Moving Vehicle | 09-Loads | No |
| Node Displacement | 09-Loads | No |
| Node Filter | 14-Tools | No |
| Node Moment | 09-Loads | No |
| Node Temperature | 09-Loads | No |
| Plate | 06-Model | No |
| Plate Filter | 14-Tools | No |
| Plate PreStress | 09-Loads | No |
| Plate Pressure | 09-Loads | No |
| Plate Property | 05-Properties | No |
| Plate Support | 07-Attributes | No |
| Plate non structural Mass | 09-Loads | No |
| Section Force | 08-Bridge | No |
| Section H Beam Property | 03-Sections | No |
| Solid Circle | 03-Sections | No |
| Staged Construction  | 04-Definitions | No |
| Stiffness | 07-Attributes | No |
| Straus Angle Section | 03-Sections | No |
| Straus BGL Angle Section | 03-Sections | No |
| Straus BGL Bulb Flat Section | 03-Sections | No |
| Straus BGL Channel Section | 03-Sections | No |
| Straus BGL Hollow Rectangle Section | 03-Sections | No |
| Straus BGL I Section | 03-Sections | No |
| Straus BGL T Section | 03-Sections | No |
| Straus BXS Section | 03-Sections | No |
| Straus Circular Hollow Section | 03-Sections | No |
| Straus Circular Solid Section | 03-Sections | No |
| Straus Cruciform Section | 03-Sections | No |
| Straus I Section | 03-Sections | No |
| Straus Library Section | 03-Sections | No |
| Straus Lipped Channel Section | 03-Sections | No |
| Straus Rectangle Hollow Section | 03-Sections | No |
| Straus Rectangle Solid Section | 03-Sections | No |
| Straus T Section | 03-Sections | No |
| Straus Top Hat Section | 03-Sections | No |
| Straus Trapezoid Hollow Section | 03-Sections | No |
| Straus Trapezoid Solid Section | 03-Sections | No |
| Straus Triangle Hollow Section | 03-Sections | No |
| Straus Triangle Solid Section | 03-Sections | No |
| Straus Z Section | 03-Sections | No |
| Straus7 R3 Export | 11-Export | No |
| String Group | 07-Attributes | No |
| Surface splitter curves | 14-Tools | No |
| T Section | 03-Sections | No |
| Table Material | 02-Materials | No |

## Valutazione funzionale

- Il vecchio `CompositeSectionComponent_OBSOLETE`, esposto come secondo `Composite Beam Property`, è stato rimosso: il componente attuale fornisce la proprietà composta e le proprietà FEM di acciaio, n0, n∞ e n ritiro. Nessun esempio versionato usa il GUID rimosso.
- `Composite Section Linear Tension Analysis` è il percorso precedente per azioni assegnate a una sezione. Per risultati FEM il percorso corrente usa `Super Element Check`; per sezioni H, H inclinate e cassoni con fasi esplicite usa `Bridge Section Check`. Il vecchio componente resta disponibile: i tre percorsi non sono sostituiti automaticamente perché una sostituzione potrebbe cambiare i risultati.
- `Section Force` e `Composite Section Self Tension` servono per controlli di sezione manuali. Le proprietà geometriche H e composte servono per interrogare e confrontare le rigidezze. Non sono duplicati dell’export.
- `Concrete Slab Section`, `Composite Beam Property` e `Creep And Shrinkage Concrete` servono a costruire le proprietà: gli esempi precedenti salvano spesso questi dati già costruiti nei parametri. Il nuovo esempio 19 contiene esplicitamente i generatori della soletta e della proprietà mista.
- Geometria, file di armature, fasi, opzioni gPC, fatica, armatura trasversale, cassoni, tau, pioli e grafici sono funzioni complementari del verificatore. L’assenza da un GHX non giustifica la loro eliminazione.

## Componenti presenti negli esempi GHX

| Componente | Esempi |
|---|---|
| Attach Moving Paths | 1 |
| Beam | 10 |
| Beam Distributed Load | 4 |
| Beam Filter | 2 |
| Beam Gradient Load | 11 |
| Beam Output Stations | 2 |
| Beam Override | 3 |
| Beam PreLoad | 11 |
| Beam Property | 4 |
| Beam Results | 17 |
| BridGHe Panel | 2 |
| Bridge Action | 16 |
| Bridge Action Combinations | 1 |
| Bridge Analysis Options | 6 |
| Bridge Beam Envelope | 1 |
| Bridge Bearing Envelope | 1 |
| Bridge Bearings | 5 |
| Bridge Check Map | 1 |
| Bridge Check Summary | 6 |
| Bridge Combination Excel | 3 |
| Bridge Connections | 9 |
| Bridge Construction Sequence | 2 |
| Bridge Deck Mesh | 2 |
| Bridge Deflection & Camber | 1 |
| Bridge Detail Check | 13 |
| Bridge Detail Layout | 2 |
| Bridge Detail Stations | 3 |
| Bridge Diaphragms | 7 |
| Bridge Displacement Envelope | 1 |
| Bridge Fatigue History | 1 |
| Bridge Force Plot | 4 |
| Bridge Girder Results | 1 |
| Bridge Intermediate Stiffener | 13 |
| Bridge Intermediate Stiffener Plot | 5 |
| Bridge Longitudinal Bracing | 4 |
| Bridge Member Assignment | 10 |
| Bridge Model Audit | 6 |
| Bridge Moving Lanes | 1 |
| Bridge Parametric Layout | 2 |
| Bridge Phase | 18 |
| Bridge Phase Model | 1 |
| Bridge Phase Table | 3 |
| Bridge Plot Preview | 8 |
| Bridge Rebar Row | 1 |
| Bridge Report | 4 |
| Bridge Result Cases | 1 |
| Bridge Result Comparison | 1 |
| Bridge Result Labels | 4 |
| Bridge Section Check | 3 |
| Bridge Section Geometry | 3 |
| Bridge Section Increment | 1 |
| Bridge Section Schedule | 4 |
| Bridge Segment Composite | 1 |
| Bridge Segment Override | 1 |
| Bridge Segment Sections | 2 |
| Bridge Segments | 16 |
| Bridge Shear Plot | 1 |
| Bridge Shrinkage Action | 16 |
| Bridge Shrinkage Increments | 1 |
| Bridge Slab Grillage | 2 |
| Bridge Span Advisor | 1 |
| Bridge Stress Plot | 4 |
| Bridge Stud Plot | 5 |
| Bridge Stud Results | 5 |
| Bridge Studs | 15 |
| Bridge Support Stiffener | 13 |
| Bridge Support Stiffener Plot | 5 |
| Bridge Verification Results | 1 |
| Build BridGHe Model | 16 |
| Build Model | 16 |
| Composite Beam Property | 4 |
| Composite Section Linear Tension Results | 10 |
| Composite Shrinkage Actions | 4 |
| Composite beam | 14 |
| Concrete | 4 |
| Concrete Slab Section | 5 |
| Creep And Shrinkage Concrete | 3 |
| Custom Material | 2 |
| Decompose Model | 23 |
| Display Settings | 5 |
| Element Id | 10 |
| Hollow Rect | 2 |
| I Section | 5 |
| Load Case | 3 |
| Load Combination | 2 |
| Moving Static Analysis | 2 |
| Node | 10 |
| Node Load | 8 |
| Node Results | 18 |
| Plate Results | 1 |
| Preview BridGHe Model | 5 |
| Reinforcement Steel B450C | 3 |
| Restraints | 10 |
| Section Geometric Property | 1 |
| Simple Bridge | 2 |
| Simple Bridge — Solid Diaphragms | 13 |
| Simple Bridge — Truss Diaphragms | 2 |
| Solid Rect | 1 |
| Steel | 4 |
| Straus R3 Analysis | 21 |
| Super Element | 11 |
| Super Element Check | 14 |
| Super Element Filter | 13 |

Dettagli riproducibili, GUID e percorsi: [component-usage.json](component-usage.json). Rigenerazione: `py tools/audit_component_usage.py`.
