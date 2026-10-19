using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.Kernel;
namespace BridGHe.Grasshopper.Display;

public static class DisplayMenu
{
    public static void Initialize(){DisplayPreferences.Load();Rhino.RhinoApp.Idle+=Install;}
    private static void Install(object sender,EventArgs args)
    {
        var editor=global::Grasshopper.Instances.DocumentEditor;if(editor?.MainMenuStrip==null)return;
        if(editor.MainMenuStrip.Items.Cast<ToolStripItem>().Any(i=>i.Name=="BridGHeMenu")){Rhino.RhinoApp.Idle-=Install;return;}
        var menu=new ToolStripMenuItem("BridGHe"){Name="BridGHeMenu"};
        menu.DropDownItems.Add("Visualizzazione…",null,(_,_)=>DisplayWindow.Open());
        menu.DropDownItems.Add("Guida",null,(_,_)=>DisplayWindow.Open(1));
        menu.DropDownItems.Add("About BridGHe",null,(_,_)=>DisplayWindow.Open(2));
        menu.DropDownItems.Add(new ToolStripSeparator());
        var enabled=new ToolStripMenuItem("Preview attiva"){Checked=DisplayPreferences.Current.Enabled,CheckOnClick=true};
        enabled.CheckedChanged+=(_,_)=>DisplayPreferences.Set(DisplayPreferences.Current with{Enabled=enabled.Checked});
        menu.DropDownOpening+=(_,_)=>enabled.Checked=DisplayPreferences.Current.Enabled;menu.DropDownItems.Add(enabled);
        editor.MainMenuStrip.Items.Add(menu);Rhino.RhinoApp.Idle-=Install;
    }
}
public sealed class DisplayWindow:Form
{
    private static DisplayWindow instance;
    private readonly TabControl tabs=new(){Dock=DockStyle.Fill};
    private readonly Dictionary<string,CheckBox> boxes=[];
    private readonly NumericUpDown symbols=new(){DecimalPlaces=3,Minimum=.001m,Maximum=100000m,Increment=.05m,Width=115};
    private readonly NumericUpDown loads=new(){DecimalPlaces=4,Minimum=.0001m,Maximum=100000m,Increment=.01m,Width=115};
    private readonly NumericUpDown transparency=new(){DecimalPlaces=2,Minimum=0,Maximum=1,Increment=.05m,Width=115};
    private readonly TextBox pattern=new(){Width=200};
    private bool syncing;
    public static void Open(int tab=0){if(instance==null||instance.IsDisposed)instance=new DisplayWindow();instance.tabs.SelectedIndex=tab;instance.Show(global::Grasshopper.Instances.DocumentEditor);instance.BringToFront();}
    public DisplayWindow()
    {
        Text="BridGHe";Size=new Size(480,700);MinimumSize=new Size(420,540);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;
        var heading=new Label{Text="BridGHe",Font=new Font("Segoe UI",18,FontStyle.Bold),Height=58,Dock=DockStyle.Top,Padding=new Padding(16,10,0,0)};
        Controls.Add(tabs);Controls.Add(heading);
        var settings=new TabPage("Visualizzazione");var list=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};settings.Controls.Add(list);tabs.TabPages.Add(settings);
        (string Name,string Label)[] switches=[("Enabled","Attiva tutte le preview BridGHe"),("Automatic","Preview automatica degli output"),("Geometry","Nodi e assi FEM"),("Sections","Profili fisici delle sezioni"),("Shaded","Superfici ombreggiate"),("Reinforcement","File di armatura schematiche"),("Loads","Carichi e percorsi mobili"),("Supports","Vincoli e appoggi elastici"),("Releases","Svincoli di estremità"),("OtherAttributes","Altri attributi"),("Axes","Assi locali"),("Labels","ID e nomi degli elementi")];
        foreach(var entry in switches){var box=new CheckBox{Text=entry.Label,AutoSize=true,Margin=new Padding(0,3,0,3)};boxes[entry.Name]=box;list.Controls.Add(box);box.CheckedChanged+=(_,_)=>Apply();}
        list.Controls.Add(new Label{Text="Moltiplicatore dei simboli / assi",AutoSize=true,Margin=new Padding(0,12,0,2)});list.Controls.Add(symbols);
        list.Controls.Add(new Label{Text="Moltiplicatore delle frecce dei carichi",AutoSize=true});list.Controls.Add(loads);
        list.Controls.Add(new Label{Text="Nome esatto del caso di carico (vuoto = tutti)",AutoSize=true});list.Controls.Add(pattern);
        list.Controls.Add(new Label{Text="Trasparenza superfici [0–1]",AutoSize=true});list.Controls.Add(transparency);transparency.ValueChanged+=(_,_)=>Apply();
        var reset=new Button{Text="Ripristina predefiniti",AutoSize=true,Margin=new Padding(0,12,0,2)};reset.Click+=(_,_)=>DisplayPreferences.Set(new());list.Controls.Add(reset);
        symbols.ValueChanged+=(_,_)=>Apply();loads.ValueChanged+=(_,_)=>Apply();pattern.TextChanged+=(_,_)=>Apply();
        AddTextTab("Guida","Modello acciaio → Beam Override con proprietà omogeneizzate → analisi Straus → Build BridGHe Model → assegnazione della sezione mista → verificatore.\r\n\r\nDisplay Settings e Preview BridGHe Model controllano una preview locale. Disattivare la preview automatica qui per evitare sovrapposizioni con gli output a monte. Il comando Preview attiva resta un interruttore generale.\r\n\r\nLe file di armatura sono tre linee schematiche per fila attiva: gli input definiscono l'area totale, non il numero o il diametro delle barre.\r\n\r\nLe sezioni generiche omogeneizzate conservano il profilo fisico acciaio + soletta. Il colore blu indica l'acciaio, il grigio la soletta, l'arancio le armature. Le impostazioni grafiche non cambiano il modello o il calcolo.");
        AddTextTab("About","BridGHe\r\nRhino 8 / .NET 8 / Windows x64\r\nModellazione, analisi Straus7 R3 e verifiche con librerie gPC.\r\n\r\nI simboli grafici dipendono dal tipo di carico o attributo supportato; la loro assenza non elimina dati dal modello. Il verificatore corrente delle travi ad H accetta N–M–V nel piano: torsione e flessione deviata sono segnalate, non scartate.");
        DisplayPreferences.Changed+=Sync;FormClosed+=(_,_)=>DisplayPreferences.Changed-=Sync;Sync();
    }
    private void AddTextTab(string name,string text){var tab=new TabPage(name);tab.Controls.Add(new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=text,Font=new Font("Segoe UI",10),BackColor=SystemColors.Window,BorderStyle=BorderStyle.None,Margin=new Padding(12)});tabs.TabPages.Add(tab);}
    private void Sync(){syncing=true;var current=DisplayPreferences.Current;foreach(var pair in boxes)pair.Value.Checked=(bool)typeof(DisplayOptions).GetProperty(pair.Key)!.GetValue(current)!;symbols.Value=(decimal)Math.Clamp(current.SymbolScale,.001,100000);loads.Value=(decimal)Math.Clamp(current.LoadScale,.0001,100000);pattern.Text=current.LoadCase;transparency.Value=(decimal)current.Transparency;syncing=false;}
    private void Apply(){if(syncing)return;var options=DisplayPreferences.Current with{SymbolScale=(double)symbols.Value,LoadScale=(double)loads.Value,LoadCase=pattern.Text,Transparency=(double)transparency.Value};foreach(var pair in boxes)typeof(DisplayOptions).GetProperty(pair.Key)!.SetValue(options,pair.Value.Checked);DisplayPreferences.Set(options);}
}
