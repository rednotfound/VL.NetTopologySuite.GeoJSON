using System.Runtime.CompilerServices;
using VL.Core.Import;

// The normalisation helpers the tests assert on are internal: they exist to catch a regression,
// not to be part of the node surface.
[assembly: InternalsVisibleTo("VL.NetTopologySuite.GeoJSON.Tests")]

// Without this every public static method is demoted to a raw .NET reflection node, which the
// NodeBrowser hides. The package still loads, compiles and packs with zero warnings, so the
// symptom is indistinguishable from the package not loading at all. Nine VL.GIS releases
// shipped that way.
//
// The namespace is stripped whole and Category put in its place, so Read GeoJSON and Write GeoJSON
// land in VL.NetTopologySuite's own NetTopologySuite.IO, beside Read WKT and Write WKT, instead of
// a category of their own (Gray Book: "Prefer existing Categories over inventing your own"). A
// satellite joining its base package's category is what vvvv's packs do - VL.Stride.TextureFX
// into Stride.Textures, VL.Audio.UI into Audio - and this exact form, Namespace plus Category, is
// how VL.Core.Commands ships: ImportAsIs(Namespace = "VL.Core", Category = "System").
[assembly: ImportAsIs(Namespace = "VL.NetTopologySuite.GeoJSON", Category = "NetTopologySuite.IO")]
