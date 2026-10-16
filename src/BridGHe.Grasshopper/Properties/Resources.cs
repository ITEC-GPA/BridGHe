using System.Drawing;
using System.IO;
using System.Collections.Concurrent;
using System;
namespace BridGHe.Grasshopper.Properties
{
    internal static class Resources
    {
        private static readonly ConcurrentDictionary<string, Lazy<Bitmap>> Cache = new();
        internal static Bitmap Load(string name) => Cache.GetOrAdd(name, key => new Lazy<Bitmap>(() =>
        {
            using Stream stream = typeof(Resources).Assembly.GetManifestResourceStream("BridGHe.Grasshopper.Resources." + key + ".png") ?? throw new FileNotFoundException(key);
            using Bitmap original = new Bitmap(stream); return new Bitmap(original);
        })).Value;
        internal static Bitmap BeamDistributedLoadType1Image => Load("BeamDistributedLoadType1Image");
        internal static Bitmap BeamDistributedLoadType2Image => Load("BeamDistributedLoadType2Image");
        internal static Bitmap BeamDistributedLoadType3Image => Load("BeamDistributedLoadType3Image");
        internal static Bitmap BeamDistributedLoadType4Image => Load("BeamDistributedLoadType4Image");
        internal static Bitmap BeamDistributedLoadType5Image => Load("BeamDistributedLoadType5Image");
        internal static Bitmap BeamDistributedLoadType6Image => Load("BeamDistributedLoadType6Image");
        internal static Bitmap BeamDistributedMomentType1Image => Load("BeamDistributedMomentType1Image");
        internal static Bitmap BeamDistributedMomentType2Image => Load("BeamDistributedMomentType2Image");
        internal static Bitmap BeamDistributedMomentType3Image => Load("BeamDistributedMomentType3Image");
        internal static Bitmap BeamDistributedMomentType4Image => Load("BeamDistributedMomentType4Image");
        internal static Bitmap BeamDistributedMomentType5Image => Load("BeamDistributedMomentType5Image");
        internal static Bitmap BeamDistributedMomentType6Image => Load("BeamDistributedMomentType6Image");
        internal static Bitmap BeamPointLoadImage => Load("BeamPointLoadImage");
        internal static Bitmap BeamPointMomentImage => Load("BeamPointMomentImage");
        internal static Bitmap LoadPatchTypeAngleSplit => Load("LoadPatchTypeAngleSplit");
        internal static Bitmap LoadPatchTypeAuto1 => Load("LoadPatchTypeAuto1");
        internal static Bitmap LoadPatchTypeAuto2 => Load("LoadPatchTypeAuto2");
        internal static Bitmap LoadPatchTypeAuto3 => Load("LoadPatchTypeAuto3");
        internal static Bitmap LoadPatchTypeAuto4 => Load("LoadPatchTypeAuto4");
        internal static Bitmap LoadPatchTypeManual => Load("LoadPatchTypeManual");
        internal static Bitmap LocalAxisImage => Load("LocalAxisImage");
    }
}
