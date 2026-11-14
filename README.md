# VL.NetTopologySuite.GeoJSON

GeoJSON for vvvv gamma, as NetTopologySuite features:

* `FromGeoJSON`: GeoJSON text to a spread of features
* `GeoJSONReader (Reactive)`: a large GeoJSON file read in the background, without freezing vvvv
* `ToGeoJSON`: features back to GeoJSON text, with an exact round trip

The features open with VL.NetTopologySuite's `Split` and `GetAttribute`, which installs with this package. Decoded geometry carries SRID 4326 (longitude, latitude), as GeoJSON requires.

For use with vvvv, the visual live-programming environment for .NET: http://vvvv.org

## Getting started
- Install as [described here](https://thegraybook.vvvv.org/reference/hde/managing-nugets.html) via commandline:

    `nuget install VL.NetTopologySuite.GeoJSON -pre`

- Usage examples and more information are included in the pack and can be found via the [Help Browser](https://thegraybook.vvvv.org/reference/hde/findinghelp.html). Start with *Explanation Overview of available nodes*, then *HowTo Read GeoJSON*.
- What changed in each version: [Releases](https://github.com/rednotfound/VL.NetTopologySuite.GeoJSON/releases)

## Related packages
- [VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite): geometry, and the `Split` / `GetAttribute` that open a feature
- [VL.Mapsui](https://github.com/rednotfound/VL.Mapsui): draw the features on a map
- [VL.PROJ](https://github.com/rednotfound/VL.PROJ): move coordinates from one coordinate system to another

## Contributing
- Report issues on [GitHub](https://github.com/rednotfound/VL.NetTopologySuite.GeoJSON/issues)
- How the package is built, tested and released, and why it is shaped the way it is: [the guide](https://github.com/rednotfound/VL.NetTopologySuite.GeoJSON/blob/main/docs/GUIDE.md)
- When making a pull-request, please make sure to read the general [guidelines on contributing to vvvv libraries](https://thegraybook.vvvv.org/reference/extending/contributing.html)

## Credits
Based on [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) and [NetTopologySuite.IO.GeoJSON4STJ](https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON), both BSD-3-Clause. The sample file of populated places is from [Natural Earth](https://www.naturalearthdata.com), public domain.

## License
[MIT](https://github.com/rednotfound/VL.NetTopologySuite.GeoJSON/blob/main/LICENSE)
