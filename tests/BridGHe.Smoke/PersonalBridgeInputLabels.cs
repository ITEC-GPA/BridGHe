using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static partial class PersonalBridgeExplicitInputs
{
    private static readonly Dictionary<string,string> Labels=new()
    {
        ["Support Stations"]="Progressive appoggi [mm]", ["Girder Offsets"]="Offset trasversali travi [mm]",
        ["Girder Levels"]="Quote assi travi [mm]", ["Deck Widths"]="Semilarghezze soletta [mm]",
        ["Deck Thickness"]="Spessore soletta [mm]", ["Crossfall"]="Pendenza trasversale [%]",
        ["Maximum Element Length"]="Lunghezza massima aste [mm]", ["Chord Tolerance"]="Tolleranza corda [mm]",
        ["Support Skew"]="Sghembo appoggi [deg]", ["Deck Beam Spacing"]="Passo aste soletta [mm]",
        ["Deck Beam Stations"]="Progressive aste soletta [mm]", ["Deck Beam Level"]="Quota asse soletta [mm]",
        ["Maximum Length"]="Lunghezza massima conci [mm]", ["Maximum Spacing"]="Passo massimo traversi [mm]",
        ["At Supports"]="Traversi agli appoggi", ["Truss Height"]="Altezza reticolo traversi [mm]",
        ["Panels per Bay"]="Pannelli reticolari per campo", ["Pattern"]="Schema reticolare",
        ["Connect X"]="Collega incrocio diagonali", ["Families"]="Famiglie aste da assegnare",
        ["Family"]="Famiglia aste della regola", ["Group"]="Gruppo aste (0 = tutti)",
        ["Segment"]="Concio o fascia (0 = tutti)", ["Start Scale"]="Scala iniziale sezione [adim.]",
        ["End Scale"]="Scala finale sezione [adim.]", ["Guide Angle"]="Angolo guida appoggi [deg]",
        ["Translation Stiffness"]="Rigidezze traslazionali [N/mm]", ["Rotation Stiffness"]="Rigidezze rotazionali [N mm/rad]",
        ["Offset"]="Offset locale asse trave [mm]", ["Super-Element"]="ID trave da selezionare",
        ["Properties name"]="Nomi proprietà da selezionare", ["Units"]="Sistema unità di misura",
        ["Solver"]="Tipo analisi Straus", ["Output Path"]="Percorso modello Straus",
        ["Analysis Results"]="Casi Straus da importare", ["Keep Running"]="Aggiornamento automatico",
        ["Consider Rebars"]="Considera armature nella sezione", ["Gravity NSM"]="Gravità su masse non strutturali",
        ["Consider Concrete"]="Considera calcestruzzo nelle fasi miste",
        ["Rebar area"]="Area armatura superiore [mm²]", ["Bottom rebar area"]="Area armatura inferiore [mm²]",
        ["Top concrete cover"]="Quota asse armatura dalla faccia superiore [mm]",
        ["Bottom concrete cover"]="Quota asse armatura dalla faccia inferiore [mm]",
        ["Direction"]="Direzione carico", ["Angle"]="Rotazione sezione [deg]",
        ["a"]="Distanza iniziale relativa a/L", ["b"]="Distanza finale relativa b/L",
        ["Axis 1"]="Gradiente deformazione asse 1", ["Axis 2"]="Gradiente deformazione asse 2",
        ["Fck"]="Resistenza cilindrica cls fck [MPa]", ["Elastic Modulus Steel"]="Modulo elastico acciaio Es [MPa]",
        ["RH"]="Umidità relativa [%]", ["t0"]="Età applicazione carico [giorni]",
        ["t"]="Età finale calcolo [giorni]", ["Cement Type"]="Classe cemento",
        ["Drying start"]="Età fine cura [giorni]", ["Reference age"]="Età attivazione misto [giorni]",
        ["Action ID"]="Identificativo azione", ["Case"]="Nome caso FEM sorgente",
        ["Primary Only"]="Solo effetto primario", ["Response"]="Risposta sezione allo strain",
        ["Behavior"]="Comportamento sezione nella fase", ["Calculation"]="Metodo assemblaggio fasi",
        ["Additional Phase Members"]="Ammetti aste aggiuntive nelle fasi", ["Tolerance"]="Tolleranza geometrica [mm]",
        ["Rows"]="Righe nel template combinazioni", ["Overwrite"]="Sovrascrivi file esistente",
        ["CountPerRow"]="Numero pioli per fila", ["Diameter"]="Diametro pioli [mm]",
        ["LongitudinalPitch"]="Passo longitudinale pioli [mm]", ["TransversePitch"]="Passo trasversale pioli [mm]",
        ["Fu"]="Resistenza ultima pioli fu [MPa]", ["GammaV"]="Coefficiente pioli gamma V",
        ["HeadDiameter"]="Diametro testa pioli [mm]", ["HeadThickness"]="Spessore testa pioli [mm]",
        ["RequiredCover"]="Copriferro richiesto [mm]", ["RepeatedActionDetail"]="Dettaglio per azioni ripetute",
        ["Layout"]="Disposizione irrigiditori", ["Width"]="Larghezza piatto sinistro [mm]",
        ["Thickness"]="Spessore piatto sinistro [mm]", ["RightWidth"]="Larghezza piatto destro [mm]",
        ["RightThickness"]="Spessore piatto destro [mm]", ["LeftPanel"]="Lunghezza pannello sinistro [mm]",
        ["RightPanel"]="Lunghezza pannello destro [mm]", ["EqualPanels"]="Pannelli di uguale lunghezza",
        ["LengthFactor"]="Coefficiente lunghezza libera", ["ExternalCompressionKN"]="Compressione esterna [kN]",
        ["LoadX"]="Eccentricità carico X [mm]", ["LoadZ"]="Eccentricità carico Z [mm]",
        ["CheckWelds"]="Verifica saldature", ["WeldThroat"]="Gola cordone saldatura [mm]",
        ["EndDistance"]="Distanza estremità trave [mm]", ["FootprintLength"]="Lunghezza impronta appoggio [mm]",
        ["FootprintWidth"]="Larghezza impronta appoggio [mm]", ["RigidEndPost"]="Montante terminale rigido",
        ["EndPostSpacing"]="Interasse montanti terminali [mm]", ["Standard"]="Norma per le verifiche",
        ["LimitState"]="Stato limite", ["Class4"]="Considera sezione in classe 4",
        ["TopFlangeBuckling"]="Instabilità ala superiore", ["BottomFlangeBuckling"]="Instabilità ala inferiore",
        ["WebBuckling"]="Instabilità anima", ["CommonLoadY"]="Quota comune carico Y [mm]",
        ["GammaM0"]="Coefficiente acciaio gamma M0", ["GammaM1"]="Coefficiente instabilità gamma M1",
        ["GammaM2"]="Coefficiente connessioni gamma M2", ["GammaC"]="Coefficiente cls gamma C",
        ["GammaS"]="Coefficiente armature gamma S", ["AlphaCC"]="Coefficiente cls alfa cc",
        ["ShearEta"]="Coefficiente taglio eta", ["Web Local Instability"]="Instabilità locale anima",
        ["Top Flange Local Instability"]="Instabilità locale ala superiore", ["Bottom Flange Local Instability"]="Instabilità locale ala inferiore",
        ["Bottom flange width"]="Larghezza ala inferiore [mm]", ["Top flange width"]="Larghezza ala superiore [mm]",
        ["Bottom flange thickness"]="Spessore ala inferiore [mm]", ["Top flange thickness"]="Spessore ala superiore [mm]",
        ["Web thickness"]="Spessore anima [mm]", ["Radius"]="Raggio raccordo sezione [mm]",
        ["Poisson Ratio"]="Coefficiente di Poisson", ["Thermal Expansion"]="Dilatazione termica [1/°C]",
        ["Use Poisson"]="Calcola G da E e Poisson", ["Density"]="Densità materiale [t/mm³]",
        ["Yielding"]="Resistenza caratteristica [MPa]", ["Strength for Steel"]="Resistenza ultima acciaio [MPa]",
        ["Kind"]="Categoria materiale", ["Up"]="Versore verticale di riferimento",
        ["Quantity"]="Grandezza da rappresentare", ["Show Min/Max Combo"]="Mostra combinazioni minime e massime",
        ["Graphic Title"]="Titolo del grafico", ["Display"]="Modalità rappresentazione grafico",
        ["Stations"]="Progressive del grafico [m]", ["Index"]="Indice elemento selezionato",
        ["Wrap"]="Riavvolgi indici fuori intervallo", ["Template"]="Percorso template Word",
        ["Title"]="Titolo del report", ["Project"]="Descrizione progetto", ["Author"]="Autore report",
        ["Revision"]="Revisione report", ["Enabled"]="Abilita componente", ["Geometry"]="Mostra geometria",
        ["Sections"]="Mostra sezioni", ["Shaded"]="Mostra superfici ombreggiate", ["Reinforcement"]="Mostra armature",
        ["Loads"]="Mostra carichi", ["Supports"]="Mostra appoggi", ["Releases"]="Mostra svincoli",
        ["OtherAttributes"]="Mostra altri attributi", ["Axes"]="Mostra assi locali", ["Labels"]="Mostra etichette",
        ["Symbol scale"]="Scala simboli", ["Load scale"]="Scala frecce carichi", ["Transparency"]="Trasparenza preview",
        ["Load case"]="Filtro caso di carico", ["Step"]="Incremento serie", ["Order"]="Ordine duplicazione dati"
    };

    private static string Label(IGH_Param p)
    {
        string owner=(p.Attributes.GetTopLevel.DocObject as GH_Component)?.GetType().Name ?? "";
        string label=(owner,p.Name) switch
        {
            ("SimpleBridgeSolidComponent","Deck Thickness")=>"Spessore soletta [mm]",
            ("BeamSectionConcreteSlabComponent","Height")=>"Spessore soletta [mm]",
            ("BeamSectionConcreteSlabComponent","Width")=>"Larghezza efficace soletta [mm]",
            ("BridgeStudsComponent","Height")=>"Altezza pioli [mm]",
            ("BridgePlotPreviewComponent","Width")=>"Larghezza anteprima [px]",
            ("BridgePlotPreviewComponent","Index")=>"Indice grafico da visualizzare",
            ("BridgeConstructionPhaseComponent","Number")=>"Numero ordine fase",
            ("BridgeConstructionPhaseComponent","Name")=>"Nome fase costruttiva",
            ("Component_DuplicateData","Data")=>"Valore ausiliario da ripetere",
            ("Component_DuplicateData","Number")=>"Numero ripetizioni",
            ("BridgeBearingsComponent","Types")=>"Tipi appoggi in ordine supporto e trave",
            ("BridgeCombinationExcelComponent","Mode")=>"Operazione Excel",
            ("BridgeCombinationExcelComponent","File")=>"Percorso Excel combinazioni",
            ("BridgeReportComponent","File")=>"Percorso report Word",
            ("BridgeConnectionsComponent","Mode")=>"Tipo collegamento eccentrico",
            ("BeamPropertyComponent","Type")=>"Formulazione elemento trave",
            ("BeamDistributedLoadComponent","Type")=>"Schema distribuzione carico",
            ("BeamPreLoadComponent","Type")=>"Tipo predeformazione o presollecitazione",
            ("MaterialSteelComponent","Grade")=>"Classe acciaio carpenteria",
            ("MaterialConcreteComponent","Grade")=>"Classe resistenza calcestruzzo",
            ("BeamSectionIComponent","Height")=>"Altezza sezione acciaio [mm]",
            ("BridgeSectionScheduleComponent","Start")=>"Progressiva inizio regola [mm]",
            ("BridgeSectionScheduleComponent","End")=>"Progressiva fine regola [mm]",
            (_,"Name")=>owner.Contains("Material")?"Nome materiale":owner=="BuildModelComponent"?"Nome modello FEM":owner=="LoadCaseComponent"?"Nome caso di carico":"Nome proprietà",
            (_,"Height")=>"Altezza sezione [mm]", (_,"Start")=>"Valore iniziale serie",
            _=>Labels.TryGetValue(p.Name,out string value)?value:p.Name
        };
        if(p is Param_String && Persistent(p)?.IsEmpty==false && Persistent(p).AllData(true).Cast<IGH_Goo>().All(g=>g is GH_String s && s.Value.Length==0)) label+=" (vuoto)";
        return label;
    }

    private static void RenameExistingControls(IGH_DocumentObject[] originals)
    {
        var inputs=originals.OfType<GH_Component>().SelectMany(c=>c.Params.Input).ToArray();
        foreach(var control in originals.OfType<IGH_Param>().Where(p=>p.SourceCount==0))
        {
            var targets=inputs.Where(p=>p.Sources.Contains(control)).ToArray();
            if(targets.Length==0 || control is not (GH_NumberSlider or GH_BooleanToggle or GH_ValueList or GH_Panel)) continue;
            var target=targets.OrderBy(p=>(p.Attributes.GetTopLevel.DocObject as GH_Component)?.Category=="Maths"?1:0).First();
            control.NickName=Label(target);
            control.Description=string.Join("\n",targets.Select(p=>p.Description).Distinct());
            control.Attributes.ExpireLayout();
        }
    }
}
