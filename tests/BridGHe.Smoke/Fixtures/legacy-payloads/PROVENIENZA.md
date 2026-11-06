# Provenienza delle fixture `legacy-payloads` (R6.6, P2U, 6/11)

Payload BGHA1 dei modelli FEM di prova, **scritti una volta sola** dal writer FEM di BridGHe (`ModelArchive.Write`) mentre esisteva, per la decisione del coordinatore del 6/11 (R6.6, punto 1). Sono fixture nuove: **non si rigenerano mai** (il generatore crea i file con `FileMode.CreateNew` e rifiuta una cartella non vuota) e non sono la rigenerazione di un atteso.

- **Commit del generatore:** BridGHe `5ebd9e9` (branch `claude/f6-bridghe-tests`, su `53c3d2b` + `3d6f714`); Checker `37619bda`, Model `bc5a7a07`, build Release `-p:GpcPilot=true`.
- **Generatore:** `tests/BridGHe.Smoke/LegacyPayloadFixtures.Fem.cs` (`LegacyPayloadFixtures.All`, `Write`), lanciato con `BRIDGHE_LEGACY_FIXTURES=write:<questa cartella>` e `--regression gpc-legacy-import` il 6/11 alle 09:57. Ogni modello scritto due volte dà gli stessi byte (46 su 46).
- **Contenuto:** 46 payload (`*.bgha1`), `index.tsv` (nome, unità, byte, SHA-256, esito del lettore di quel commit: `ACCEPTED` o `REFUSED <tipo>: <messaggio>`), `units-axes.tsv` (assi locali di prima, `BeamModel.GetLocalAxes(true)`, delle travi del modello `units-*`, per bit).
  - `gpc-legacy-import`: `units-<U>`, `bridge-identities-<U>`, `-twice-<U>`, `-two-<U>` per U = Nmm, Knm, SI, IPS;
  - `ImportDump` (stessi id del suo corpus): `nodes-<U>`, `nodes-labelled-rigid-<U>`, `beams-<U>`, `plates-<U>` per le quattro unità, `beams-duplicate-<releases|taper|super-elements|members|prestrain>`;
  - rifiuti del lettore in più: spostamenti imposti (nodo vincolato e nodo libero), collegamento rigido sul proprio nodo, due riferimenti di vincolo, superelemento senza nome, appoggio di trave senza rigidezza, gioco su una molla bilaterale, sezione specchiata, massa di piastra in kN-m.
- **Lettura:** `tests/BridGHe.Smoke/LegacyPayloadFixtures.cs` (nessuna classe FeMM; controlla dimensione e SHA-256 dell'indice). Fino a R6.5 `GpcLegacyImportTests.Fem.cs` verifica che il writer FEM dia ancora gli stessi byte e gli stessi assi.
- **Esiti di rifiuto registrati (18):**
- `bridge-identities-two-Nmm`: REFUSED ArgumentException: LegacyBeamInTwoSuperElements: beam 1 is in the super elements 'G1' and 'G2'; a beam belongs to one super element.
- `bridge-identities-two-Knm`: REFUSED ArgumentException: LegacyBeamInTwoSuperElements: beam 1 is in the super elements 'G1' and 'G2'; a beam belongs to one super element.
- `bridge-identities-two-SI`: REFUSED ArgumentException: LegacyBeamInTwoSuperElements: beam 1 is in the super elements 'G1' and 'G2'; a beam belongs to one super element.
- `bridge-identities-two-IPS`: REFUSED ArgumentException: LegacyBeamInTwoSuperElements: beam 1 is in the super elements 'G1' and 'G2'; a beam belongs to one super element.
- `beams-duplicate-releases`: REFUSED ArgumentException: Cannot assign more than 1 set of releases for beam 2
- `beams-duplicate-taper`: REFUSED ArgumentException: LegacyDuplicateBeamTaper
- `beams-duplicate-super-elements`: REFUSED ArgumentException: LegacyBeamInTwoSuperElements: beam 2 is in the super elements 'SE-1' and 'SE-3'; a beam belongs to one super element.
- `beams-duplicate-members`: REFUSED ArgumentException: LegacyDuplicateBridgeMemberAttribute: beam 2
- `beams-duplicate-prestrain`: REFUSED ArgumentException: Beam 2 has multiple prestrain in loadcase T
- `refusal-prescribed-restrained`: REFUSED NotSupportedException: LegacyPrescribedDisplacementNotMappedYet
- `refusal-prescribed-free`: REFUSED NotSupportedException: LegacyNodeLoadNotMappedYet: NodeDisplacementLoadModel
- `refusal-rigid-collapse`: REFUSED ArgumentException: Rigid link collapses to the same node within model tolerance.
- `refusal-restraint-frames`: REFUSED ArgumentException: Node 1: conflicting/invalid restraint coordinate systems.
- `refusal-empty-super-element`: REFUSED ArgumentException: LegacyEmptySuperElementName
- `refusal-support-stiffness`: REFUSED ArgumentException: Beam support requires finite positive stiffness [F/L2] and finite gap [L].
- `refusal-support-gap`: REFUSED ArgumentException: A gap requires a compression-only spring.
- `refusal-mirrored-section`: REFUSED NotSupportedException: LegacyMirroredSectionNotMappedYet
- `refusal-plate-mass-Knm`: REFUSED NotSupportedException: LegacyPlateNonStructuralMassUnitsRequireNmm

## SHA-256 dei file

| File | Byte | SHA-256 |
|---|---|---|
| `beams-duplicate-members.bgha1` | 164405 | `B685286E6E2672FA4A61C1660350D289B874FDBA92D3C1AAADA2CE74AEFAC809` |
| `beams-duplicate-prestrain.bgha1` | 162926 | `2042CA06D480EEFDBA8E5B67F25A015561310492F95E3AB373F0974DC83937FB` |
| `beams-duplicate-releases.bgha1` | 177293 | `94DED391A0CD7ABB23AB9F973023B47A2C879BE1300B09D12CF6D1995A0FCCA1` |
| `beams-duplicate-super-elements.bgha1` | 162942 | `1CA47E0D6FAFCE188D27A3CC48C9571A2FD2A2E337F7C023212B5A31EE91165B` |
| `beams-duplicate-taper.bgha1` | 166943 | `D75D231965B9E45EA23AFEF405461BE633829659303DE4AB4FD9DE81DFDD2DDC` |
| `beams-IPS.bgha1` | 161543 | `FF63E451AF4D7CC59F566C4C0E5498BEE9CB674B6E90B7EAC4C0D624FCC8A349` |
| `beams-Knm.bgha1` | 161543 | `D1A6C2EF57F8533EEE97CA35C3FD0709AAA3D2DE25AF0B914998D1EE8700724C` |
| `beams-Nmm.bgha1` | 161543 | `5BE064274FAE644421E895664506D43AC33AAAF1F6F7B81F7A8BE39635EE88AA` |
| `beams-SI.bgha1` | 161542 | `F2D3DA334FBAD9DCB2281D81B8BB64C62C871CDF8B1F5D8392C90184DD089B7A` |
| `bridge-identities-IPS.bgha1` | 83992 | `C4023B8AD9789331C40C775DF5B4415AB0494E0334D14E360191F1873F3D7209` |
| `bridge-identities-Knm.bgha1` | 83992 | `546D8949622108C722003E7C8E7BC3359CDAFF0E2D6D8F4F343A6F68CA91FF22` |
| `bridge-identities-Nmm.bgha1` | 83992 | `3BBC487DD2BB3E604E33EBE6A57DC2D0D8EA7E08636DBFFBADAFB6FF78493D5E` |
| `bridge-identities-SI.bgha1` | 83990 | `E9146AAA3D0642581EF65337933CF1B1511B8E9F4CE60CB67F653B2164D3031F` |
| `bridge-identities-twice-IPS.bgha1` | 85391 | `3C3817F29D5ACEB083C1C6A820C254CAD757A79017DC6B3DA296F8BDC615A8C9` |
| `bridge-identities-twice-Knm.bgha1` | 85391 | `24342A3DEF79678FA85C7871D95BA85C357DABA8B215F648B3009D4080D52527` |
| `bridge-identities-twice-Nmm.bgha1` | 85391 | `ECDBC51910292314E1C3D5A160BB91CB73A0B3945498A472DACD427344BAE5D8` |
| `bridge-identities-twice-SI.bgha1` | 85389 | `5DB832739D91B49E50BD61FAF389A95B6274844A1BE97C3EC362632E1561E07D` |
| `bridge-identities-two-IPS.bgha1` | 85391 | `BC33E94DD5B6D1A9A99CFAB4AC3B15B1F1E7B2BA6C66F71E9F4AE4957A9BFC08` |
| `bridge-identities-two-Knm.bgha1` | 85391 | `611BA67171D0FD2F827540CFF19D828299A2267EA6E27D250393EC48967CEA93` |
| `bridge-identities-two-Nmm.bgha1` | 85391 | `1785B04EA6C9A1384697BC21DFE813B39CF0D205AFB678A62849D4D3C388DE6E` |
| `bridge-identities-two-SI.bgha1` | 85389 | `E42B6F376D60359D91C399C14A60DCC32EA7CEF0A9579D430C226338A79A93CC` |
| `index.tsv` | 6341 | `1A071FACF9E67A77BC1F43D4C365B06F9F3D152EDD08DF64159B548620C50564` |
| `nodes-IPS.bgha1` | 117219 | `E64B4C100E078979E20755A72486B910201149F6D7C2ECF64A724744084BECB9` |
| `nodes-Knm.bgha1` | 117219 | `F863DAD54D35751857447F536ADCE627429A444C296FC017B8CF257B52D21AB2` |
| `nodes-labelled-rigid-IPS.bgha1` | 118609 | `049F0CA5ECF81AC6507FD76781C2F36AEA58F4AB620D58BE6694F648A8D3F1A4` |
| `nodes-labelled-rigid-Knm.bgha1` | 118609 | `5FBBB38D7452F6BA83E379B86B40119F38CF618B088D0CF3B2D03DF9953B00F9` |
| `nodes-labelled-rigid-Nmm.bgha1` | 118609 | `5216C5548F34E7282E33EDAA2E981637D8C5EDAEB1D93A0C9BB94F6D5855CFAC` |
| `nodes-labelled-rigid-SI.bgha1` | 118608 | `09EBC93D7D9FC0A223C7E30B00A8B3E989E0A8CEA38C01AA5CAECD48FB1A9C58` |
| `nodes-Nmm.bgha1` | 117219 | `12ED9FAA33B201490B96C8A8B53094AF7C433712347C6BBF7A56BFF863F5B49B` |
| `nodes-SI.bgha1` | 117218 | `D14E6766B5DED4D42B1DB7C52DFFE5FB3FE574ADB10521C0D1F0E4D8D4DD0916` |
| `plates-IPS.bgha1` | 99834 | `9228EA44C738B5C3AD02C281CD40141FDCBF3001A7025412670CB89B0DD7C9FA` |
| `plates-Knm.bgha1` | 99834 | `7F4D60CD871D334FEE16E583F4E4BEDD3647CC4046E672166F2DFF6782248D29` |
| `plates-Nmm.bgha1` | 99834 | `148AC240BCC709072E6A9AC8459136727DF5D3B70E5883770415C7B4E6D4BBAA` |
| `plates-SI.bgha1` | 99833 | `E6A6C1C549E14D635F90C1FC8AA30197A3D3EB8B197FEE1F166699F28846D5F7` |
| `refusal-empty-super-element.bgha1` | 44698 | `9F59EF01AD4B1854D8F5AFFC1B2999C8FA67AE3FCEAE1C78E7D8C0D2F4DB01F3` |
| `refusal-mirrored-section.bgha1` | 43302 | `530050A03C26BE393AD66447B8F1B81D9115CF0FC0B448EA4C03F15D7C99F82B` |
| `refusal-plate-mass-Knm.bgha1` | 108478 | `F3E0B9C67A4705479F23C3F61A462E2202B575D6F136800A0356783AF686DFF0` |
| `refusal-prescribed-free.bgha1` | 52878 | `D0C5B96AEE1EE6B65AC0B599F9F8B2DEE53EA6BBE6415E47C4AB52008F1F2ED4` |
| `refusal-prescribed-restrained.bgha1` | 52878 | `95DE731CB21432865122CC18817E4087C66E462EB117DB3A149091DA97BE7BD3` |
| `refusal-restraint-frames.bgha1` | 51695 | `59710EB71C3856F6074ED2F7480A6DFA3A4963C1CDA931916C30254E348AC199` |
| `refusal-rigid-collapse.bgha1` | 45132 | `183A273AB5213F6319C5C320DE4C273D5E45ABB0BC13791620E2C40A3B067299` |
| `refusal-support-gap.bgha1` | 48039 | `9CCCAFB153A867558CD708E9FC9CA99A125390EA60AAB37F5AFB40D5ADB11240` |
| `refusal-support-stiffness.bgha1` | 48031 | `879EDD84AF679AAFA1422F01CC8184AEBD9F10B2B6000765E7EB03EA1876B2FF` |
| `units-axes.tsv` | 1925 | `07FA63C6489977C00B9FB9FA141AB855AC464F554618CAE80C470A4E7746506D` |
| `units-IPS.bgha1` | 142019 | `A0CE36B6B98F23F56E1C2ADE472C27EABFDB20F8C2F29710F1E0BC5B24AD5D22` |
| `units-Knm.bgha1` | 142019 | `88549FD69EA47B2B14AB410B54C1ABC301882C6DF08931B6D8ECEC495959F9EA` |
| `units-Nmm.bgha1` | 142019 | `0DB901FA2F8D1BCE4948AF255DD361705C89AD071C93A8A2097491D8ACB452AD` |
| `units-SI.bgha1` | 142017 | `15B6565105F1AC1883BB7B6944290D1C6EF0018993E16E64353D10523E94B78B` |