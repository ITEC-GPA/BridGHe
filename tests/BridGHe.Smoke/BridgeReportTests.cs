using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Reports;
using GPC.Checkers.CompositeBridge;

internal static class BridgeReportTests
{
    private static int checks;
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal static readonly string Template = Path.GetFullPath("example/48-report-word/Template_BridGHe.docx");
    private static void Require(bool ok, string text) { if (!ok) throw new Exception("REPORT: " + text); checks++; }
    private static string Part(string path, string name) { using var zip = ZipFile.OpenRead(path); using var read = new StreamReader(zip.GetEntry(name).Open()); return read.ReadToEnd(); }
    private static string Text(string path) => string.Join(" ", XDocument.Parse(Part(path, "word/document.xml")).Descendants(W + "t").Select(t => t.Value));
    internal static void Run()
    {
        checks = 0; string folder = Path.GetFullPath(Path.Combine("artifacts/report-tests", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        var plain = BridgeDetailPlotTests.Result();
        var stages = plain.Analysis.Stages.ToList();
        stages[^1] = plain.Stage with { Shear = plain.Stage.Shear with { Checks = new() {
            new("Intermedio · A", 8, 10, "MPa", .8), new("Intermedio · B", 0, 1, "—", 2), new("Intermedio · C", 3, 4, "MPa", null, "Controllo mancante") } } };
        var result = new BridgeStressResult(plain.Analysis with { Stages = stages }, stages.Count - 1, new("B1", 200, "C1", "G1"),new[]{BridgeCheckAdapter.PlanarAssumption});
        var data = BridgeReportData.Create(new[] { new BridgeReportEntry(result, "{2;4}[0]"), new BridgeReportEntry(result.WithSource(result.Source with { Combination = "C2" }), "{2;4}[1]") },
            new("Verifiche del ponte", "Progetto <A> & B", "Tecnico", "03"));
        Require(data.Samples.Length == 2 && data.Sections.Length == 1, "Lost samples or duplicate section configurations.");
        Require(data.Rows.Any(r => r.Check.Ratio == 2 && r.Check.Demand == 0 && r.Check.Resistance == 1), "Native qualitative WR recomputed as D/R.");
        int missingPerResult=result.Stage.Points.Count(p=>p.Active&&(!p.Utilization.HasValue||!double.IsFinite(p.Utilization.Value)))
            +BridgeGpc.Checks(result.Stage).Count(c=>!c.Ratio.HasValue||!double.IsFinite(c.Ratio.Value));
        Require(data.Rows.Count(r => r.Check.Active && !r.Check.HasRatio) == 2*missingPerResult, "Missing ratios became zero or disappeared.");
        Require(data.Governing(false).Count(r => r.Check.Name == "Intermedio · B") == 2, "Governing ties or combinations lost.");
        Require(BridgeReportData.ResultIds(new[]{"R00003","R00001","R00002"})=="R00001–R00003" && BridgeReportData.ResultIds(new[]{"R00001","R00003","R00004"})=="R00001, R00003, R00004","Result ID ranges must not invent missing results.");
        Require(data.Samples[0].Source.StationMm == 200 && data.Samples[0].TreePosition == "{2;4}[0]", "Lost local station or tree identity.");
        Require(data.Samples[0].Contributions.Select(c=>(c.N,c.Mx,c.V,c.ShrinkageStrain)).SequenceEqual(result.Stage.Contributions.Select(c=>(c.N,c.Mx,c.V,c.ShrinkageStrain))), "Report altered concomitant actions.");
        Require(data.DataHash == BridgeReportData.Create(new[] { new BridgeReportEntry(result, "{2;4}[0]"), new BridgeReportEntry(result.WithSource(result.Source with { Combination = "C2" }), "{2;4}[1]") },data.Metadata).DataHash,"Numeric hash depends on export time/ID.");
        string full = Path.Combine(folder,"full.docx");
        var exported = BridgeWordReport.Write(Template,full,data,new[]{BridgeReportTopic.Sections,BridgeReportTopic.Checks,BridgeReportTopic.Critical},overwrite:true);
        string text = Text(full);
        Require(text.Contains("Ipotesi del verificatore") && text.Contains(BridgeCheckAdapter.PlanarAssumption) && data.Samples.All(s=>s.Assumptions.Contains(BridgeCheckAdapter.PlanarAssumption)),"Planar force exclusions must be visible and archived in the report, irrespective of selected chapters.");
        Require(text.Contains("non disponibili") && text.Contains("R00001") && text.Contains("R00002"), "Incomplete checks or source IDs absent from document.");
        Require(!text.Contains("{{BG.") && text.Contains("Progetto <A> & B"), "Unreplaced/unsafe XML tokens.");
        Require(!text.Contains("Modello di riferimento") && !text.Contains("Tabulato completo"), "Unselected chapters leaked into document.");
        Require(text.Contains("Riepilogo e copertura") && text.Contains("Non" , StringComparison.OrdinalIgnoreCase), "Mandatory coverage is missing.");
        Require(XDocument.Parse(Part(full,"word/header1.xml")).Descendants(W+"t").Select(t=>t.Value).Aggregate("",(a,b)=>a+b).Contains("Progetto <A> & B"),"Split-run scalar in header not replaced.");
        Require(Part(full,"word/styles.xml")==Part(Template,"word/styles.xml"),"Template styles changed.");
        var bodyElements=XDocument.Parse(Part(full,"word/document.xml")).Root.Element(W+"body").Elements().ToArray();
        Require(!bodyElements.Zip(bodyElements.Skip(1),(a,b)=>a.Name==W+"tbl"&&b.Name==W+"tbl").Any(adjacent=>adjacent),
            "Word would merge consecutive geometry/material tables and repeat the wrong header.");
        Require(XNode.DeepEquals(XDocument.Parse(Part(full,"word/document.xml")).Descendants(W+"sectPr").Last(),XDocument.Parse(Part(Template,"word/document.xml")).Descendants(W+"sectPr").Last()),"Margins or header/footer references changed.");
        using(var archive=JsonDocument.Parse(Part(full,"bridghe/report.json")))
        {
            var saved=archive.RootElement.GetProperty("Data");
            Require(saved.GetProperty("DataHash").GetString()==data.DataHash && saved.GetProperty("Samples").GetArrayLength()==2,"Embedded numerical snapshot missing.");
            Require(saved.GetProperty("Samples")[0].GetProperty("Checks").EnumerateArray().Any(c=>c.GetProperty("Name").GetString()=="Intermedio · C" && c.GetProperty("Ratio").ValueKind==JsonValueKind.Null),"Null ratio corrupted in archive.");
        }
        byte[] before=File.ReadAllBytes(full); MustFail(()=>BridgeWordReport.Write(Template,full,data),"overwrite protection");
        Require(before.SequenceEqual(File.ReadAllBytes(full)),"Existing report changed after rejected overwrite.");
        using(var locked=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.None))
            MustFail(()=>BridgeWordReport.Write(Template,full,data,overwrite:true),"locked destination");
        Require(before.SequenceEqual(File.ReadAllBytes(full)),"Locked destination changed.");
        MustFail(()=>BridgeWordReport.Write(Template,Template,data,overwrite:true),"template protection");
        MustFail(()=>BridgeWordReport.Write(Template,full,data,new[]{(BridgeReportTopic)999},overwrite:true),"invalid topic");
        string basic=Modified("basic",d=> {
            foreach(var p in d.Descendants(W+"p").Where(p=>string.Concat(p.Descendants(W+"t").Select(t=>t.Value)).StartsWith("{{BG.") && !p.Ancestors(W+"tbl").Any()).ToArray())
                if(string.Concat(p.Descendants(W+"t").Select(t=>t.Value)) is not "{{BG.Title}}" and not "{{BG.Project}}")p.Remove();
            d.Root.Element(W+"body").Elements(W+"sectPr").Last().AddBeforeSelf(new XElement(W+"p",new XElement(W+"r",new XElement(W+"t","{{BG.Report}}"))));
        });
        string minimal=Path.Combine(folder,"minimal.docx");BridgeWordReport.Write(basic,minimal,data,Array.Empty<BridgeReportTopic>(),overwrite:true);
        Require(Text(minimal).Contains("Riepilogo e copertura")&&!Text(minimal).Contains("Verifiche governanti per voce"),"Empty selection did not retain summary alone.");
        string wrong=Modified("wrong",d=>d.Descendants(W+"t").First(t=>t.Value=="{{BG.Summary}}").Value="{{BG.Summry}}");
        MustFail(()=>BridgeWordReport.Write(wrong,full,data,overwrite:true),"missing mandatory block");
        wrong=Modified("unknown",d=>d.Descendants(W+"t").First(t=>t.Value=="{{BG.Summary}}").AddAfterSelf(new XElement(W+"t","{{BG.Fake}}")));
        MustFail(()=>BridgeWordReport.Write(wrong,full,data,overwrite:true),"unknown marker");
        string duplicate=Modified("duplicate",d=> {var p=d.Descendants(W+"p").Single(p=>string.Concat(p.Descendants(W+"t").Select(t=>t.Value))=="{{BG.Summary}}");p.AddAfterSelf(new XElement(p));});
        MustFail(()=>BridgeWordReport.Write(duplicate,full,data,overwrite:true),"duplicate block");
        string dotx=Path.Combine(folder,"basic.dotx");File.Copy(basic,dotx,true);
        using(var zip=ZipFile.Open(dotx,ZipArchiveMode.Update))
        {
            var entry=zip.GetEntry("[Content_Types].xml");string value;using(var reader=new StreamReader(entry.Open()))value=reader.ReadToEnd();
            entry.Delete();using var writer=new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open());writer.Write(value.Replace("wordprocessingml.document.main+xml","wordprocessingml.template.main+xml"));
        }
        BridgeWordReport.Write(dotx,minimal,data,Array.Empty<BridgeReportTopic>(),overwrite:true);
        Require(Part(minimal,"[Content_Types].xml").Contains("wordprocessingml.document.main+xml")&&!Part(minimal,"[Content_Types].xml").Contains("wordprocessingml.template.main+xml"),"DOTX not converted to DOCX content type.");
        Require(!Directory.EnumerateFiles(folder,".bridghe-report-*").Any(),"Failed writes leaked temporary files.");
        Console.WriteLine($"REPORT PASS: {checks} assertions; native values, missing WR, ties, template styles/headers, chapter selection, split markers, archives and safe file handling.");

        void MustFail(Action action,string label) {try {action();}catch(Exception e) when(e is IOException or ArgumentException or UnauthorizedAccessException){checks++;return;}throw new Exception("REPORT expected failure: "+label);}
        string Modified(string name,Action<XDocument> edit)
        {
            string path=Path.Combine(folder,name+".docx");File.Copy(Template,path,true);
            using var zip=ZipFile.Open(path,ZipArchiveMode.Update);var entry=zip.GetEntry("word/document.xml");XDocument xml;
            using(var s=entry.Open())xml=XDocument.Load(s);edit(xml);entry.Delete();using(var s=zip.CreateEntry("word/document.xml").Open())xml.Save(s);return path;
        }
    }
}
