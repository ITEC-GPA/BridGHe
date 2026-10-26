# Inventario dei componenti 0.2.0

220 componenti e 8 parametri istanziati nel runtime Rhino 8. GUID completi in `migration/current-components.json`. Nomi, descrizioni e opzionalità sono estratti dai componenti compilati.

## BridGHe / 01-Parameters

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Beam | parameter | — | — |
| CoordinateSystem | parameter | — | — |
| Group | parameter | — | — |
| Load Combinations | parameter | — | — |
| Material | parameter | — | — |
| Node | parameter | — | — |
| Plate | parameter | — | — |
| Section | parameter | — | — |

## BridGHe / 02-Materials

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Aluminium | component | Name; Type; Alloy | Material |
| Concrete | component | Name; Grade | Material |
| Custom Material | component | Name; Kind; Units; Modulus; Poisson Ratio; Thermal Expansion; Shear Modulus; Use Poisson; Density; Yielding; Strength for Steel | Material |
| Reinforcement Steel B450C | component | Name | Material; Information |
| Steel | component | Name; Grade | Material |
| Table Material | component | Name; Use Poisson; Poisson Ratio; Shear Modulus; Thermal Expansion; Density; Stresses; Strains | Material |

## BridGHe / 03-Sections

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Angle Section | component | Width; Flange thickness; Height; Web thickness; Double Sections; Gap | Section |
| Brep Section | component | Breps | Section |
| Bridge Read Section Excel | component | Fabrication Plan; File | Count; Segment IDs; Girder IDs; Counts per Girder; Section Table; File; Information |
| Bridge Section Excel Template | component | Fabrication Plan; File; Base Property; Overwrite | Count; Segment IDs; Girder IDs; Counts per Girder; Section Table; File; Information |
| Bridge Segment Excel | component | Fabrication Plan; File; Mode; Base Property; Overwrite | Count; Segment IDs; Girder IDs; Counts per Girder; Section Table; File; Information |
| Bridge Segment Sections | component | Fabrication Plan; Base Property; Section Table | Schedule; Properties; Sections; Segment IDs; Resolved Table; Top Offsets; Information |
| C Section | component | Width; Flange thickness; Height; Web thickness; Double Section; Gap | Section |
| Concrete Slab Section | component | Height; Width; Rebar area; Top concrete cover; Bottom rebar area; Bottom concrete cover | Concrete Slab |
| Generic Section | component | Area; Second moment 1; Second moment 2; Torsion constant; Shear L1; Shear L2; Shear A1; Shear A2; Bending modulus 1; Bending modulus 2; Offset 1; Offset 2; Angle | Section |
| Hollow Circle | component | Diameter; Thickness | Section |
| Hollow Rect | component | Height; Base; Horizontal Thickness; Vertical Thickness | Section |
| I Section | component | Bottom flange width; Top flange width; Height; Bottom flange thickness; Top flange thickness; Web thickness; Radius | Section |
| Section Geometric Property | component | Section Property | Name; Area; xg; yg; Jxx; Jyy; Angle; J₁₁; J₂₂; W₁₁; W₂₂; ρ₁₁; ρ₂₂; J |
| Section H Beam Property | component | Section H Beam Property | Name; Area; xg; yg; Jxx; Jyy; Angle; J₁₁; J₂₂; J; Height; Top Flange Width; Bottom Flange Width; Web Thickness; Top Flange Thickness; Bottom Flange Thickness |
| Solid Circle | component | Diameter | Section |
| Solid Rect | component | Height; Base | Section |
| Straus Angle Section | component | B; D; T1; T2 | Section |
| Straus BGL Angle Section | component | D; B; tw; tf; Rr; Rh; Rt1; Rt2; Rt3; Rt4; alpha1; alpha2 | Section |
| Straus BGL Bulb Flat Section | component | D; B; t; Rr; Rh; Rt1; Rt2; Rt3; alpha | Section |
| Straus BGL Channel Section | component | D; B1; B2; tw; tf1; tf2; Rr1; Rr2; Rh1; Rh2; Rt1; Rt2; Rt3; Rt4; alpha1; alpha2 | Section |
| Straus BGL Hollow Rectangle Section | component | D; B; tw; tf; Ri; Ro | Section |
| Straus BGL I Section | component | D; B1; B2; tw; tf1; tf2; Rr1; Rr2; Rt1; Rt2; Rt3; Rt4; alpha1; alpha2 | Section |
| Straus BGL T Section | component | D; B; tw; tf; Rr; Rt1; Rt2; Rt3; alpha1; alpha2 | Section |
| Straus BXS Section | component | BXS file; Length unit | Section |
| Straus Circular Hollow Section | component | D; T | Section |
| Straus Circular Solid Section | component | D | Section |
| Straus Cruciform Section | component | B; D; T1; T2 | Section |
| Straus I Section | component | B1; B2; D; T1; T2; T3 | Section |
| Straus Library Section | component | Library; Item; BGL; Length unit | Section |
| Straus Lipped Channel Section | component | B; D; L; T1; T2; T3 | Section |
| Straus Rectangle Hollow Section | component | B; D; T1; T2 | Section |
| Straus Rectangle Solid Section | component | B; D | Section |
| Straus T Section | component | B; D; L; T1; T2; T3 | Section |
| Straus Top Hat Section | component | B; D; L; T1; T2; T3 | Section |
| Straus Trapezoid Hollow Section | component | B1; B2; D; T1; T2 | Section |
| Straus Trapezoid Solid Section | component | B1; B2; D | Section |
| Straus Triangle Hollow Section | component | B; D; T1; T2 | Section |
| Straus Triangle Solid Section | component | B; D | Section |
| Straus Z Section | component | B1; B2; D; T1; T2; T3 | Section |
| T Section | component | Flange width; Flange thickness; Height; Web thickness | Section |

## BridGHe / 04-Definitions

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Bridge Action Combinations | component | Phases; Action IDs; Names; Coefficients | Table; Omitted actions |
| Bridge Combination Excel | component | Models; Phases; File; Mode; Rows; Overwrite | File; Table; Action IDs; Summary |
| Group | component | Name; Parent | Group |
| Load Case | component | Name; Gravity NSM | LoadCase |
| Load Combination | component | Name; LoadCases; Coefficients | LoadCombination |
| Staged Construction  | component | Name; Groups; Load Combinations; Morph; Rotate clusters | Staged Construction |

## BridGHe / 05-Properties

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Beam Geometric Property | component | Beam Property | Name; Area; xg; yg; Jxx; Jyy; Angle; J₁₁; J₂₂; W₁₁; W₂₂; ρ₁₁; ρ₂₂; J |
| Beam Property | component | Name; Type; Section; Material | Property |
| Composite Beam Property | component | Name; Concrete Slab Section; Steel Section; Concrete Material; Steel Material; Reinforcement Material; n0; n ∞; n ritiro; Consider Concrete; Consider Rebars | Composite Section; Only Steel Property; n0 Beam Section; n ∞ Beam Section; n ritiro Beam Section |
| Composite Geometric Property | component | Composite Beam Property; n; Consider Concrete; Consider Rebars; Consider Steel Section | Name; Area; xg; yg; Jxx; Jyy; Angle; J₁₁; J₂₂; Sxx; Syy; J |
| Plate Property | component | Name; Type; Membrane Thickness; Bending Thickness; Material | Property |

## BridGHe / 06-Model

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Beam | component | Line; Property; Groups; Angle; Offset | Beam |
| Bridge Action | component | Action ID; Model; Case; Behavior; Beam IDs | Action |
| Bridge Alignment Curve | component | Length; Radius; Origin; Heading; Custom Alignment | Alignment |
| Bridge Alignment Inputs | component | Alignment; Support Stations; Span Count; End Supports; Joint Stations; Maximum Segment Length; Maximum Element Length; Station Tolerance | Alignment; Span Lengths; Start Overhang; End Overhang; Support Stations; Support Points; Span Count; Joint Stations; Maximum Segment Length; Maximum Element Length; Length; Information |
| Bridge Bearing Pattern | component | Support Stations; Girder Offsets; Bearing Types | Bearing Types |
| Bridge Bearings | component | Bridge Geometry; Supports; Girders; Types; Guide Angle; Translation Stiffness; Rotation Stiffness; Orientation | Nodes; Frames; Labels |
| Bridge Bracing Stations | component | Alignment; Support Stations; Maximum Panel Length | Bracing Stations |
| Bridge Connection Beams | component | Connection Axes; Connection Property | Elements; Information |
| Bridge Connections | component | Mode; Connections; Property; Beams; Offset | Elements; Information |
| Bridge Construction Sequence | component | Model; Phase; Active Groups; Physical IDs; Active Bearing IDs; Enable Bearings; Girder Property; Load Increments | Phase Model; Native Stage; Phase; Active Physical IDs; Available Groups; Information |
| Bridge Crossfall Table | component | Crossfall Table | Percentages; Stations |
| Bridge Deck Mesh | component | Bridge Geometry; Plate Property; Longitudinal Mesh Length; Transverse Mesh Length; Pour Boundaries; Triangles | Bridge Geometry; Mesh; Plates; Connection Lines; Stations; Information |
| Bridge Deck Widths | component | Girder Offsets; Left Cantilever; Right Cantilever | Side Widths |
| Bridge Diaphragm Choice | component | Use Truss; Solid Diaphragms; Truss Diaphragms | Diaphragms |
| Bridge Diaphragms | component | Stations; Maximum Spacing; At Supports; Truss Height; Panels per Bay; Pattern; Connect X | Diaphragms |
| Bridge FEM Members | component | Bridge Geometry; Family Properties; Section Rules | Beams; Physical IDs; Unassigned; Information |
| Bridge Fabrication Segments | component | Alignment; Girder Axes; Joint Sets; Maximum Segment Length | Fabrication Plan; Segment Count; Segment IDs; Start Stations; End Stations; Segment Lengths |
| Bridge Family Property | component | Property; Family | Family Property |
| Bridge Geometry Assembly | component | Alignment; Support Lines; Girder Axes; Slab Layout; Fabrication Plan; Diaphragms | Bridge Definition |
| Bridge Girder Axes | component | Transverse Offsets; Axis Levels | Girder Axes; Girder Count |
| Bridge Grillage Mesh | component | Bridge Definition; Mesh Settings; Labels | Members; Member IDs; Longitudinals; Diaphragm Members; Nodes; Support Points; Joint Points; Deck; Deck Edges; Frames; Stations; Topology; Segment Ranges; Information; Bridge Geometry; Deck Beams; Deck Connections; Deck Beam Stations; Deck Strip Widths; Grillage Geometry; Deck Member IDs |
| Bridge Joint Stations | component | Joint Stations; Girder IDs | Joint Set |
| Bridge Joint Tree | component | Joint Tree | Joint Sets |
| Bridge Longitudinal Bracing | component | Bridge Geometry; Stations; Bays; Pattern; Normal Offset; Connect X; Omitted Panels | Bridge Geometry; Added Members; Information |
| Bridge Member Assignment | component | Bridge Geometry; Default Properties; Families; Schedule | Beams; Physical IDs; Unassigned; Information |
| Bridge Mesh Settings | component | Maximum Element Length; Chord Tolerance; Slab Beam Spacing; Slab Beam Stations | Mesh Settings |
| Bridge Normalize Joint Stations | component | Alignment; Joint Stations; Station Tolerance | Joint Stations |
| Bridge Parametric Layout | component | Span Count; Span Length; Span Lengths; Girder Count; Girder Spacing; Girder Offsets; Left Overhang; Right Overhang; Start Overhang; End Overhang; Radius; Origin; Heading; Custom Alignment; Bracing Spacing; Bearing Types Override | Alignment; Support Stations; Girder Offsets; Deck Widths; Span Lengths; Bearing Types; Length; Information; Bracing Stations |
| Bridge Phase | component | Number; Name; Section State; Actions; Day | Phase |
| Bridge Phase Model | component | Model; Name; Beam IDs; Property; Clear loads | Model |
| Bridge Phase Table | component | Phases | Phases; Number; Name; Day; Section state; Action ID; Behavior; Source; Case; Strain |
| Bridge Regular Girders | component | Girder Count; Girder Spacing; Explicit Offsets | Transverse Offsets |
| Bridge Rigid Links | component | Connection Axes | Elements; Information |
| Bridge Section Schedule | component | Property; Start; End; Family; Group; Segment; Start Scale; End Scale | Schedule |
| Bridge Segment Composite | component | Beams; Segment IDs; Composite Properties | Girders; Composite Properties; Segment IDs; Beam IDs |
| Bridge Segment Override | component | Beams; Segment IDs; Properties; Preserve Mass | Girders; Other Steel; Beam IDs; Information |
| Bridge Segments | component | Joints; Maximum Length | Segments |
| Bridge Shrinkage Action | component | Action ID; Model; Case; Strain increment; Beam IDs; Primary Only; Response | Action |
| Bridge Slab Grillage | component | Grillage Geometry; Concrete Material; Name | Beams; Properties; Strip Widths; Member IDs; Information |
| Bridge Slab Layout | component | Side Widths; Thickness; Crossfall Percent; Crossfall Stations; Slab Axis Level | Slab Layout |
| Bridge Solid Diaphragms | component | Stations; Maximum Spacing; At Supports | Diaphragms |
| Bridge Span Advisor | component | Alignment; Forbidden Regions; Minimum Span; Maximum Span; Target Span; Candidate Grid; Clearance; Alternatives; Pier Penalty; End Span Ratio; Required Supports | Support Stations; Support Points; Span Lengths; Scores; Information |
| Bridge Span Stations | component | Span Count; Uniform Span Length; Span Lengths; Start Overhang; End Overhang | Support Stations; Span Lengths; Alignment Length |
| Bridge State Override | component | Beams; Segment IDs; Composite Sections; States; Response; Mixed Policy; Zero Policy; Preserve Mass | Girders; Other Steel; Beam IDs; Information |
| Bridge Support Lines | component | Alignment; Support Stations; Skew Angles | Support Lines; Span Lengths; Support Centres |
| Bridge Support Stations | component | Alignment; Support Stations; Equal Span Count; End Supports; Station Tolerance | Alignment; Span Lengths; Start Overhang; End Overhang; Support Stations; Support Points; Span Count; Length; Information |
| Bridge Truss Diaphragms | component | Stations; Maximum Spacing; At Supports; Truss Height; Panels per Bay; Pattern; Connect X | Diaphragms |
| Build BridGHe Model | component | Models; Phases; Combinations; Calculation; Additional Phase Members | Model; Phases; Diagnostics |
| Build Model | component | Elements; Name; Units; Load Combinations; Staged constructions | Model |
| Composite beam | component | Beam; Section Start; Section End; Groups | Beam |
| Decompose Model | component | Model | Nodes; Beams; Plates |
| Merge Model | component | Elements; Tolerance | BridGHe Nodes; BridGHe Beams; BridGHe Plates |
| Node | component | Point | Node |
| Plate | component | Mesh; Property; Groups; Angle; Offset | Plate |
| Simple Bridge | component | Alignment; Support Stations; Girder Offsets; Girder Levels; Deck Widths; Deck Thickness; Crossfall; Segments; Diaphragms; Maximum Element Length; Chord Tolerance; Support Skew; Labels; Deck Beam Spacing; Deck Beam Stations; Deck Beam Level; Truss Diaphragms | Members; Member IDs; Longitudinals; Diaphragm Members; Nodes; Support Points; Joint Points; Deck; Deck Edges; Frames; Stations; Topology; Segment Ranges; Information; Bridge Geometry; Deck Beams; Deck Connections; Deck Beam Stations; Deck Strip Widths; Grillage Geometry; Deck Member IDs |
| Simple Bridge — Solid Diaphragms | component | Alignment; Support Stations; Girder Offsets; Girder Levels; Deck Widths; Deck Thickness; Crossfall; Segments; Diaphragms; Maximum Element Length; Chord Tolerance; Support Skew; Labels; Deck Beam Spacing; Deck Beam Stations; Deck Beam Level | Members; Member IDs; Longitudinals; Diaphragm Members; Nodes; Support Points; Joint Points; Deck; Deck Edges; Frames; Stations; Topology; Segment Ranges; Information; Bridge Geometry; Deck Beams; Deck Connections; Deck Beam Stations; Deck Strip Widths; Grillage Geometry; Deck Member IDs |
| Simple Bridge — Truss Diaphragms | component | Alignment; Support Stations; Girder Offsets; Girder Levels; Deck Widths; Deck Thickness; Crossfall; Segments; Diaphragms; Maximum Element Length; Chord Tolerance; Support Skew; Labels; Deck Beam Spacing; Deck Beam Stations; Deck Beam Level | Members; Member IDs; Longitudinals; Diaphragm Members; Nodes; Support Points; Joint Points; Deck; Deck Edges; Frames; Stations; Topology; Segment Ranges; Information; Bridge Geometry; Deck Beams; Deck Connections; Deck Beam Stations; Deck Strip Widths; Grillage Geometry; Deck Member IDs |

## BridGHe / 07-Attributes

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Beam Output Stations | component | Beam; OutputStations | Beam |
| Beam Releases | component | Beam; Axial N1; Axial N2; Shear 1 N1; Shear 2 N1; Shear 1 N2; Shear 2 N2; Torsion N1; Torsion N2; Moment 1 N1; Moment 2 N1; Moment 1 N2; Moment 2 N2 | Beam |
| Beam Support | component | Beam; Stiffness; Direction; Compression only; Gap | Beam |
| Beam Taper | component | Beam; NFromX; NFromY; NToX; NToY; AlignmentX; AlignmentY | Beam |
| Bridge Beam Offsets | component | Beams; Local Offset | Beams; Information |
| Element Id | component | Element; ID | Element |
| Load Patch Type | component | Plate; Type; Edges; Weights | Plate |
| Plate Support | component | Plate; Stiffness; Compression only; Face | Plate |
| Restraints | component | Node; Transational; Rotation | Node |
| Stiffness | component | Node; Transational; Rotational | Node |
| String Group | component | Beam; String group ID | Beam |
| Super Element | component | Element; Name | Element |

## BridGHe / 08-Bridge

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Bridge Analysis Options | component | Standard; LimitState; Class4; TopFlangeBuckling; BottomFlangeBuckling; WebBuckling; CommonLoadY; GammaM0; GammaM1; GammaM2; GammaC; GammaS; AlphaCC; ShearEta | Detail |
| Bridge Box Options | component | Enabled; BracingThickness; SpanLength; DiaphragmSpacing; DiaphragmKind; DiaphragmThickness; BracingArea; BracingRadius; BracingBucklingFactor; DistributedTorque; ConcentratedTorque; SupportDiaphragmThickness; SupportTorqueKNm; BearingSpacing | Detail |
| Bridge Connection Fatigue | component | Enabled; MinimumFlow; MaximumFlow; EquivalenceFactor; DynamicFactor; TensileFlange; FlangeStressRange; GammaFf; GammaMfStud; GammaMfFlange | Detail |
| Bridge Detail Layout | component | Bridge Geometry; Section Geometry; Girders; Start; End; Steel Top Offset; Studs; Intermediate Stiffener; Support Stiffener; Stiffener Pitch; Stiffener Stations | Stud Axes; Stud Heads; Stiffener Plates; Details; IDs; Stations; Information |
| Bridge Intermediate Stiffener | component | Enabled; Layout; Width; Thickness; RightWidth; RightThickness; LeftPanel; RightPanel; EqualPanels; LengthFactor; ExternalCompressionKN; LoadX; CheckWelds; WeldThroat | Detail |
| Bridge Rebar Row | component | Enabled; Diameter; Pitch; AxisDistance | Detail |
| Bridge Section Geometry | component | SlabWidth; SlabHeight; WebHeight; WebThickness; TopWidth; TopThickness; BottomWidth; BottomThickness; SecondBottomEnabled; SecondBottomWidth; SecondBottomThickness; SectionType; WebOffset; WebSpacing | Detail |
| Bridge Section Increment | component | Name; Active; Kind; Reference; ForceKN; MomentKNm; ShearKN; TorsionKNm; AdditionalConnectionFlow; ShrinkageMicrostrain; HomogenizationSource; Phi; PsiL; N | Detail |
| Bridge Shrinkage Increments | component | Days; Cumulative strain; Initial strain | Increment; Days; Total increment |
| Bridge Studs | component | Enabled; CountPerRow; Diameter; Height; LongitudinalPitch; TransversePitch; Fu; GammaV; HeadDiameter; HeadThickness; RequiredCover; RepeatedActionDetail | Detail |
| Bridge Support Stiffener | component | Enabled; Layout; Width; Thickness; RightWidth; RightThickness; Location; LeftPanel; RightPanel; ReactionKN; EndDistance; LoadX; LoadZ; FootprintLength; FootprintWidth; LengthFactor; RigidEndPost; EndPostSpacing; CheckWelds; WeldThroat | Detail |
| Bridge Transverse Reinforcement | component | Enabled; TopDiameter; TopPitch; BottomDiameter; BottomPitch; CotTheta; LeftFlowFraction; BendingSteelPerMetre; AnchorageLength; GoodBond; LeftConcreteEdge; RightConcreteEdge; EdgeUBar; UBarDiameter | Detail |
| Creep And Shrinkage Concrete | component | Fck; Elastic Modulus Steel; Concrete Area; RH; u; t0; t; Cement Type; Drying start; Reference age | n0; n ∞; y(t,t0); εcs; Total shrinkage; Drying shrinkage; Autogenous shrinkage; Notional size |
| Section Force | component | Name; Axial Force; Shear 2; Shear 3; Torsion; Bending 2; Bending 3; Station; NormalizedLength | Force |

## BridGHe / 09-Loads

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Attach Moving Paths | component | Model; Paths | Model |
| Beam Distributed Load | component | Beam; LoadCase; Type; Coordinate System; Direction; P1; PA; PB; P2; a; b | Beam |
| Beam Distributed Moment | component | Beam; LoadCase; Type; Direction; P1; PA; PB; P2; a; b | Beam |
| Beam Gradient Load | component | Beam; LoadCase; Axis 1; Axis 2 | Beam |
| Beam Point Load | component | Beam; LoadCase; Coordinate System; P1; P2; P3; a | Beam |
| Beam Point Moment | component | Beam; LoadCase; Coordinate System; M1; M2; M3; a | Beam |
| Beam PreLoad | component | Beam; LoadCase; Type; Value | Beam |
| Bridge Moving Combinations | component | Base table; Moving cases; Base combination; Traffic factor; Isolated positions; Action prefix | Table; Action IDs; Cases; Mapping |
| Bridge Moving Lanes | component | Prototype; Offsets; Cases | Paths |
| Bridge Straight Moving Path | component | Grillage Geometry; Template; Case; Lane offset; Divisions | Path; Axis; Step |
| Composite Beam Strain | component | Beam; LoadCase; Strain | Beam |
| Composite Shrinkage Actions | component | Composite property; Concrete strain | Straus pre-strain; Axis 2 thermal gradient; Free centroid strain; Free curvature |
| Moving Centrifugal Data | component | K0; K1; Limit K1; Minimum K1; Maximum K1; Length unit; Force unit | Moving object |
| Moving Distributed Force | component | X1; X2; Y1; Y2; Distributed force; Mobility; Axes; Adjacency; Centrifugal | Moving object |
| Moving Heat Source | component | X; Y; Size X; Size Y; Heat | Moving object |
| Moving Library Template | component | Library; Item | Moving object |
| Moving Load Path | component | Template; Load case name; Start; End; Lateral; Shape; Surface; Target; Divisions; Generate static cases | Moving object |
| Moving Load Template | component | Name; Vehicles; Lane factors; Multilane type; Transition load; Beam lateral tolerance; Relative tolerance; Minimum lane width; Centrifugal data | Moving object |
| Moving Point Force | component | X; Y; Force; Mobility; Axes; Adjacency; Centrifugal | Moving object |
| Moving Static Analysis | component | Model; Output path; Run | Model; Cases |
| Moving Vehicle | component | Name; Loads; Instance; Direction; Velocity; Start time; Lanes; Vehicle set | Moving object |
| Node Displacement | component | Node; LoadCase; Translation; Rotation | Node |
| Node Load | component | Node; LoadCase; Force | Node |
| Node Moment | component | Node; LoadCase; Moment | Node |
| Node Temperature | component | Node; LoadCase; Type; Value | Node |
| Plate PreStress | component | Plate; LoadCase; Px; Py; Pz | Plate |
| Plate Pressure | component | Plate; LoadCase; Coordinate System; Face; Value; Projected | Plate |
| Plate non structural Mass | component | Plate; LoadCase; Mass; Dyn factor; Offset | Plate |

## BridGHe / 10-Checks

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Bridge Detail Check | component | Section result; Studs; Intermediate stiffener; Support stiffener; Options; Additional details | Section result; Check; Demand; Resistance; Units; Ratio; Status; Warnings |
| Bridge Section Check | component | Geometry; Concrete; Steel; Rebar material; Phases; Top rebars; Bottom rebars; Details; Options | Results; Phases; Warnings; Native analysis |
| Composite Section Linear Tension Analysis | component | Beams; Loads Only steel Phase; Loads Infinte Time Phase; Loads Instant Time Phase; Homogenization coefficient Infinite; Homogenization coefficient Istant; Top Flange Subject To Local Instability; Web Subject To Local Instability; Bottom Flange Subject To Local Instability; Consider concrete | Only Steel Buckling; Infinite Buckling; Total Buckling; TauB |
| Composite Section Self Tension | component | Composite Beam Property; Homogenization coefficient; Strain | Result |
| Super Element Check | component | BridGHe Model; Super-Element Beam; Web Local Instability; Top Flange Local Instability; Bottom Flange Local Instability | Tension Check; Tau b; Tension Forces |

## BridGHe / 11-Export

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Straus7 R3 Export | component | Model; Output Path | Status |

## BridGHe / 12-Analysis

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Bridge Analysis Cases | component | Model; Required Cases; Solver | Analysis Results; Information |
| Straus R3 Analysis | component | Model; Solver; Analysis Results; Output Path; Keep Running | Model |

## BridGHe / 13-Results

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Beam Results | component | Beam | Load cases; Stations; Steps; Shear Forces; Bending Moments; Axial Forces; Torques |
| Bridge Beam Envelope | component | Model; Element IDs; Cases; Component; Mode | Element ID; Station; Value; Case; Step; Step type; Concomitant; Result; Coverage; Units |
| Bridge Bearing Envelope | component | Model; Element IDs; Cases; Component; Mode | Element ID; Station; Value; Case; Step; Step type; Concomitant; Result; Coverage; Units |
| Bridge Check Map | component | Model; Results; Beam IDs; Stations; Cases; Labels | Axes; Colours; Beam IDs; Maximum Ratios; Unavailable; Governing; Information |
| Bridge Check Summary | component | Results; Labels | Label; Phase; Check; Ratio; Status; Units; Notes; Maximum finite ratio; Unavailable ratios; Governing labels |
| Bridge Deflection & Camber | component | Model; Bridge Geometry; Girder; Case; Step; Step Type; Direction; Camber Factor; Relative Camber; Display Scale | Stations; Absolute Displacement; Relative Deflection; Camber; Deflected Curve; Camber Curve; Source; Information |
| Bridge Detail Stations | component | Model; Results; Locations; Phases; Up; Tolerance | Results; Compression kN; Signed reaction kN; End type; Points |
| Bridge Displacement Envelope | component | Model; Element IDs; Cases; Component; Mode | Element ID; Station; Value; Case; Step; Step type; Concomitant; Result; Coverage; Units |
| Bridge Fatigue History | component | Values; Model; Beam ID; Station; Ordered Cases; Force Component; Response Factors; Response Units; Exponent; Reference Cycles | History; Sources; Ranges; Means; Cycle Counts; Equivalent Range; Units; Information |
| Bridge Force Plot | component | BridGHe Model; Super-Element Beam; Results; Quantity; Combinations; Show Min/Max Combo; File Path; Graphic Title; Keep Running | SVG; File; Minimum; Maximum |
| Bridge Girder Results | component | Beams; Results; Girder | Beams; Results; Stations; Available Girders |
| Bridge Intermediate Stiffener Plot | component | BridGHe Model; Super-Element Beam; Results; Check; Combinations; Show Min/Max Combo; File Path; Graphic Title; Keep Running; Display; Stations | SVG; File; Minimum; Maximum; Governing checks; Unavailable checks |
| Bridge Report | component | Results; Template; File; Topics; Model; Title; Project; Author; Revision; SVG; Overwrite | File; Summary; Warnings; Report ID |
| Bridge Result Cases | component | Model | Cases; Case by step; Step; Step type; Result type; Count; Units |
| Bridge Result Comparison | component | Reference keys; Reference values; Candidate keys; Candidate values; Absolute tolerance; Relative tolerance | All within tolerance; Key; Difference; Relative difference; Within tolerance |
| Bridge Result Labels | component | Results; Format | Labels |
| Bridge Shear Plot | component | BridGHe Model; Super-Element Beam; Results; Quantity; Combinations; Show Min/Max Combo; File Path; Graphic Title; Keep Running | SVG; File; Minimum; Maximum |
| Bridge Shear Results | component | Section result | Shear; Average tau; Maximum tau; Equivalent stress; Web resistance; Warnings |
| Bridge Stress Plot | component | BridGHe Model; Super-Element Beam; Results; Quantity; Combinations; Show Min/Max Combo; Additional values; File Path; Graphic Title; Keep Running | SVG; File; Minimum; Maximum |
| Bridge Stud Plot | component | BridGHe Model; Super-Element Beam; Results; Quantity; Combinations; Show Min/Max Combo; File Path; Graphic Title; Keep Running | SVG; File; Minimum; Maximum |
| Bridge Stud Results | component | Section result | Connection flow; Force per stud; Resistance per stud; Resistance per length; Force ratio; Phase; Phase flow; Warnings |
| Bridge Support Stiffener Plot | component | BridGHe Model; Super-Element Beam; Results; Check; Combinations; Show Min/Max Combo; File Path; Graphic Title; Keep Running; Display; Stations | SVG; File; Minimum; Maximum; Governing checks; Unavailable checks |
| Bridge Verification Results | component | Section result | Check; Demand; Resistance; Units; Ratio; Status; Notes |
| Composite Section Linear Tension Results | component | Linear Tension Results | Point1_ConcreteEpsilon; Point1_ConcreteSigma; Point2_RebarSigma; Point3_RebarSigma; Point4_ConcreteSigma; Point5_SteelSigma; Point6_SteelSigma; Point6_SteelTau ; Point6_SigmaId ; Point7_SteelSigma; Point7_SteelTau ; Point7_SigmaId ; Point8_SteelSigma; Point8_SteelEpsilon |
| Node Results | component | Node | Load cases D; Steps D; Displacements; Rotations; Load cases F; Steps F; Forces; Moments |
| Plate Results | component | Plate | Load cases; Nodes; Steps; Force; Force Max; Force Min; Force Angle; Force Von Mises; Moment; Moment Max; Moment Min; Moment Angle; Shear Force; Shear Force Max; Shear Force Angle |

## BridGHe / 14-Tools

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| Align beam axes | component | Beam; Axis; Direction | Beam |
| Align plate axes | component | Plate; Axis; Direction | Plate |
| Beam Filter | component | Beams; IDs; Ini Pos; End Pos; Units; Properties name; Groups | Beams; Others |
| Beam Moment Dispatch | component | Beams; Case; Tolerance; Component; Sign | Positive; Negative; Mixed; Zero; Missing; States; Information |
| Beam Override | component | Beam; Property; Groups; Angle; Offset | Beam |
| Beam Property Override | component | Beams; Original Properties; Replacement Properties | Beams; Replaced; Information |
| Bridge Model Audit | component | Model; Tolerance; Bridge Geometry; Phase Models | No input errors; Severity; Code; Element; Description; Model |
| Elements Filter | component | Elements; Find | Found; Others |
| Get Beam Property | component | Beam | Property |
| Merge Geometry | component | Elements; Tolerance | Points; Curves; Meshes |
| Node Filter | component | Nodes; IDs; Positions; Units; Groups | Nodes; Others |
| Plate Filter | component | Plates; IDs; Positions; Units; Properties name; Groups | Plates; Others |
| Super Element Filter | component | BridGHe Model; Super-Element | Beams |
| Surface splitter curves | component | Surface; Points; Lines; Tolerance | Cuttings |

## BridGHe / 15-Preview

| Nome | Tipo | Ingressi | Uscite |
|---|---|---|---|
| BridGHe Panel | component | — | Help |
| Bridge Plot Preview | component | SVG; Index; Width | SVG |
| Display Settings | component | Enabled; Geometry; Sections; Shaded; Reinforcement; Loads; Supports; Releases; OtherAttributes; Axes; Labels; Symbol scale; Load scale; Transparency; Load case | Settings |
| Preview BridGHe Model | component | Model / elements; Settings | Info |
